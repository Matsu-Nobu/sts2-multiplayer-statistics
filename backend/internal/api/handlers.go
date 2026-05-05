package api

import (
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"time"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
	"github.com/go-chi/chi/v5"
	"github.com/google/uuid"
)

// --- POST /sessions ---

type createSessionReq struct {
	HostName    *string `json:"host_name,omitempty"`
	HostSteamID *string `json:"host_steam_id,omitempty"`
	CharacterID *string `json:"character_id,omitempty"`
	Ascension   *int    `json:"ascension,omitempty"`
	Seed        *string `json:"seed,omitempty"`
}

type createSessionRes struct {
	SessionID  string `json:"session_id"`
	WriteToken string `json:"write_token"`
	ShareURL   string `json:"share_url"`
}

func (s *Server) createSession(w http.ResponseWriter, r *http.Request) {
	var in createSessionReq
	if r.ContentLength > 0 {
		if err := json.NewDecoder(r.Body).Decode(&in); err != nil {
			writeError(w, http.StatusBadRequest, "invalid json body")
			return
		}
	}

	id := uuid.NewString()
	token := uuid.NewString()

	err := s.Store.CreateSession(r.Context(), store.CreateSessionInput{
		ID:          id,
		WriteToken:  token,
		HostName:    in.HostName,
		HostSteamID: in.HostSteamID,
		CharacterID: in.CharacterID,
		Ascension:   in.Ascension,
		Seed:        in.Seed,
	})
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to create session")
		return
	}

	// Pre-register the host as a known player so future filters work even if
	// they never appear in turns/events explicitly.
	if in.HostSteamID != nil && *in.HostSteamID != "" {
		display := *in.HostSteamID
		if in.HostName != nil && *in.HostName != "" {
			display = *in.HostName
		}
		_ = s.Store.UpsertPlayer(r.Context(), *in.HostSteamID, display)
	}

	writeJSON(w, http.StatusCreated, createSessionRes{
		SessionID:  id,
		WriteToken: token,
		ShareURL:   s.BaseURL + "/s/" + id,
	})
}

// --- POST /sessions/{id}/turns ---

type turnReq struct {
	CombatIndex int                     `json:"combat_index"`
	TurnNumber  int                     `json:"turn_number"`
	IsFinal     bool                    `json:"is_final"`
	Timestamp   string                  `json:"timestamp"`
	Players     map[string]turnReqEntry `json:"players"`
}

type turnReqEntry struct {
	PlayerName string `json:"player_name"`
	// turn / combat are arbitrary blobs validated by the mod side. We store the entire body verbatim.
}

func (s *Server) postTurn(w http.ResponseWriter, r *http.Request) {
	id := chi.URLParam(r, "id")

	body, err := io.ReadAll(r.Body)
	if err != nil {
		writeError(w, http.StatusBadRequest, "failed to read body")
		return
	}
	var in turnReq
	if err := json.Unmarshal(body, &in); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body")
		return
	}
	if in.CombatIndex < 1 || in.TurnNumber < 1 {
		writeError(w, http.StatusBadRequest, "combat_index and turn_number must be >= 1")
		return
	}

	if err := s.Store.UpsertTurn(r.Context(), id, in.CombatIndex, in.TurnNumber, in.IsFinal, body); err != nil {
		writeError(w, http.StatusInternalServerError, "failed to store turn")
		return
	}
	for steamID, p := range in.Players {
		display := p.PlayerName
		if display == "" {
			display = steamID
		}
		_ = s.Store.UpsertPlayer(r.Context(), steamID, display)
	}

	w.WriteHeader(http.StatusNoContent)
}

// --- POST /sessions/{id}/events ---

type eventReq struct {
	EventUUID  string          `json:"event_uuid"`
	EventType  string          `json:"event_type"`
	OccurredAt string          `json:"occurred_at"`
	PlayerID   *string         `json:"player_id,omitempty"`
	Floor      *int            `json:"floor,omitempty"`
	Payload    json.RawMessage `json:"payload"`
}

func (s *Server) postEvents(w http.ResponseWriter, r *http.Request) {
	id := chi.URLParam(r, "id")

	var in []eventReq
	if err := json.NewDecoder(r.Body).Decode(&in); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body (expected array)")
		return
	}
	for i, ev := range in {
		if ev.EventUUID == "" || ev.EventType == "" || ev.OccurredAt == "" {
			writeError(w, http.StatusBadRequest, "events[].event_uuid, event_type, occurred_at are required")
			return
		}
		payload := ev.Payload
		if len(payload) == 0 {
			payload = json.RawMessage("{}")
		}
		_, err := s.Store.InsertEvent(r.Context(), store.EventInput{
			EventUUID:  ev.EventUUID,
			SessionID:  id,
			PlayerID:   ev.PlayerID,
			EventType:  ev.EventType,
			OccurredAt: ev.OccurredAt,
			Floor:      ev.Floor,
			Payload:    payload,
		})
		if err != nil {
			writeError(w, http.StatusInternalServerError, "failed to store event")
			return
		}

		// Side effect: run_end → update session outcome / final_floor / finished_at.
		if ev.EventType == "run_end" {
			s.applyRunEnd(r, id, ev)
		}
		_ = i
	}

	w.WriteHeader(http.StatusNoContent)
}

type runEndPayload struct {
	Outcome    string `json:"outcome"`
	FinalFloor *int   `json:"final_floor,omitempty"`
}

func (s *Server) applyRunEnd(r *http.Request, sessionID string, ev eventReq) {
	var p runEndPayload
	if err := json.Unmarshal(ev.Payload, &p); err != nil {
		return
	}
	finishedAt := ev.OccurredAt
	if finishedAt == "" {
		finishedAt = time.Now().UTC().Format(time.RFC3339)
	}
	_ = s.Store.UpdateRunOutcome(r.Context(), sessionID, p.Outcome, p.FinalFloor, finishedAt)
}

// --- GET /api/sessions/{id} ---

type sessionDoc struct {
	Session *store.Session    `json:"session"`
	Players []store.Player    `json:"players"`
	Turns   []json.RawMessage `json:"turns"`
	Events  []json.RawMessage `json:"events"`
}

func (s *Server) getSession(w http.ResponseWriter, r *http.Request) {
	id := chi.URLParam(r, "id")
	sess, err := s.Store.GetSession(r.Context(), id)
	if errors.Is(err, store.ErrSessionNotFound) {
		writeError(w, http.StatusNotFound, "session not found")
		return
	}
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to load session")
		return
	}
	turns, err := s.Store.ListTurns(r.Context(), id)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to load turns")
		return
	}
	events, err := s.Store.ListEvents(r.Context(), id)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to load events")
		return
	}
	players, err := s.Store.ListPlayersForSession(r.Context(), id)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to load players")
		return
	}
	if turns == nil {
		turns = []json.RawMessage{}
	}
	if events == nil {
		events = []json.RawMessage{}
	}
	if players == nil {
		players = []store.Player{}
	}
	writeJSON(w, http.StatusOK, sessionDoc{
		Session: sess,
		Players: players,
		Turns:   turns,
		Events:  events,
	})
}

// --- helpers ---

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}

func writeError(w http.ResponseWriter, status int, msg string) {
	writeJSON(w, status, map[string]string{"error": msg})
}
