package api

import (
	"encoding/json"
	"errors"
	"log"
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
	// they never appear in events explicitly.
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

// --- POST /sessions/{id}/turns (Phase 3.5: deprecated) ---

// postTurnGone always returns 410. Phase 3.5 で集計 payload を廃止し、
// すべての記録は POST /events に統合された。古い mod クライアント向けの
// 明示的なシグナル。
func postTurnGone(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusGone, map[string]string{
		"error":  "POST /turns is no longer supported (Phase 3.5)",
		"hint":   "update the StsStats mod and use POST /sessions/{id}/events instead",
	})
}

// --- POST /sessions/{id}/events ---

type eventReq struct {
	EventUUID    string          `json:"event_uuid"`
	EventType    string          `json:"event_type"`
	OccurredAt   string          `json:"occurred_at"`
	PlayerID     *string         `json:"player_id,omitempty"`
	Floor        *int            `json:"floor,omitempty"`
	CombatIndex  *int            `json:"combat_index,omitempty"`
	TurnNumber   *int            `json:"turn_number,omitempty"`
	Sequence     *int            `json:"sequence,omitempty"`
	Payload      json.RawMessage `json:"payload"`
}

func (s *Server) postEvents(w http.ResponseWriter, r *http.Request) {
	id := chi.URLParam(r, "id")

	var in []eventReq
	if err := json.NewDecoder(r.Body).Decode(&in); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body (expected array)")
		return
	}
	for _, ev := range in {
		if ev.EventUUID == "" || ev.EventType == "" || ev.OccurredAt == "" {
			writeError(w, http.StatusBadRequest, "events[].event_uuid, event_type, occurred_at are required")
			return
		}
		payload := ev.Payload
		if len(payload) == 0 {
			payload = json.RawMessage("{}")
		}
		// Side effect: combat_start → 同じ floor で別 combat_index を持つ既存 events を削除。
		// セーブ＆中断で戦闘最初からやり直すと combat_index が新規で振られるため、
		// 階数が同じ古い戦闘記録は上書きする（mod がそういう仕様で再 emit してくる）。
		// InsertEvent より前に走らせて、新 combat_start 自体を消さないようにする。
		if ev.EventType == "combat_start" && ev.Floor != nil && ev.CombatIndex != nil {
			if _, derr := s.Store.DeleteCombatEventsAtFloorExcept(r.Context(), id, *ev.Floor, *ev.CombatIndex); derr != nil {
				// 失敗してもこの event 自体の挿入は継続（ベストエフォート）
				log.Printf("[postEvents] dedup at floor=%d failed: %v", *ev.Floor, derr)
			}
		}

		_, err := s.Store.InsertEvent(r.Context(), store.EventInput{
			EventUUID:   ev.EventUUID,
			SessionID:   id,
			PlayerID:    ev.PlayerID,
			EventType:   ev.EventType,
			OccurredAt:  ev.OccurredAt,
			Floor:       ev.Floor,
			CombatIndex: ev.CombatIndex,
			TurnNumber:  ev.TurnNumber,
			Sequence:    ev.Sequence,
			Payload:     payload,
		})
		if err != nil {
			writeError(w, http.StatusInternalServerError, "failed to store event")
			return
		}

		// Side effect: run_end → update session outcome / final_floor / finished_at.
		if ev.EventType == "run_end" {
			s.applyRunEnd(r, id, ev)
		}

		// Side effect: run_start → player_name から players テーブルに UpsertPlayer。
		// POST /sessions の host_name は host だけ登録するため、MP の peer (non-host)
		// は別経路で名前を登録しないと display_name="" のまま UI に出てしまう。
		// mod 側で各プレイヤーに run_start を emit する際 player_name を含めている。
		if ev.EventType == "run_start" && ev.PlayerID != nil && *ev.PlayerID != "" {
			var rs struct {
				PlayerName string `json:"player_name"`
			}
			if json.Unmarshal(payload, &rs) == nil && rs.PlayerName != "" {
				_ = s.Store.UpsertPlayer(r.Context(), *ev.PlayerID, rs.PlayerName)
			}
		}
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

	etag, err := s.Store.ComputeSessionETag(r.Context(), id)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to compute etag")
		return
	}
	w.Header().Set("ETag", etag)
	if match := r.Header.Get("If-None-Match"); match != "" && match == etag {
		w.WriteHeader(http.StatusNotModified)
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
	if events == nil {
		events = []json.RawMessage{}
	}
	if players == nil {
		players = []store.Player{}
	}
	writeJSON(w, http.StatusOK, sessionDoc{
		Session: sess,
		Players: players,
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
