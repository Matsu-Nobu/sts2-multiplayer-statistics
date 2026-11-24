package api

import (
	"encoding/json"
	"net/http"
	"time"

	"github.com/google/uuid"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
)

// --- POST /imports/check ---

const maxImportCheckRuns = 1000

type importCheckRun struct {
	Seed       string   `json:"seed"`
	StartedAt  string   `json:"started_at"`
	PlayerIDs  []string `json:"player_ids"`
	Outcome    string   `json:"outcome"`
	FinalFloor *int     `json:"final_floor,omitempty"`
}

// checkImports は、セーブデータの履歴にあるランのうち、サーバにまだ無いランの番号を返す (docs/spec/api.md「POST /imports/check」)。
// 既にあるセッションの結果が空なら、送られてきた結果で埋める。
func (s *Server) checkImports(w http.ResponseWriter, r *http.Request) {
	var in struct {
		Runs []importCheckRun `json:"runs"`
	}
	if err := json.NewDecoder(r.Body).Decode(&in); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body")
		return
	}
	if len(in.Runs) > maxImportCheckRuns {
		writeError(w, http.StatusBadRequest, "too many runs")
		return
	}
	missing := []int{}
	for i, run := range in.Runs {
		started, err := time.Parse(time.RFC3339, run.StartedAt)
		if err != nil {
			writeError(w, http.StatusBadRequest, "runs[].started_at must be RFC3339")
			return
		}
		same, err := s.Store.SameRun(r.Context(), run.Seed, started, run.PlayerIDs)
		if err != nil {
			writeError(w, http.StatusInternalServerError, "failed to look up runs")
			return
		}
		if same == nil {
			missing = append(missing, i)
			continue
		}
		if same.Outcome != nil || !validOutcome(run.Outcome) {
			continue
		}
		filled, err := s.Store.FillRunOutcome(r.Context(), same.ID, run.Outcome, run.FinalFloor, time.Now().UTC().Format(time.RFC3339))
		if err != nil {
			writeError(w, http.StatusInternalServerError, "failed to update run")
			return
		}
		if filled {
			s.warmSummaries(same.ID)
		}
	}
	writeJSON(w, http.StatusOK, map[string][]int{"missing": missing})
}

func validOutcome(o string) bool {
	return o == "victory" || o == "death" || o == "abandoned"
}

// --- POST /imports/run ---

// 取り込む 1 ランの上限 (docs/spec/api.md「POST /imports/run」)。実際のランより十分大きく、偽のランでディスクを埋めにくい値にする。
const (
	maxImportPlayers = 4
	maxImportFloors  = 100
	maxImportEvents  = maxImportPlayers + maxImportFloors + 1
)

type importRunReq struct {
	Session struct {
		HostName    *string  `json:"host_name"`
		HostSteamID *string  `json:"host_steam_id"`
		CharacterID *string  `json:"character_id"`
		Ascension   *int     `json:"ascension"`
		Seed        string   `json:"seed"`
		GameVersion *string  `json:"game_version"`
		StartedAt   string   `json:"started_at"`
		PlayerIDs   []string `json:"player_ids"`
	} `json:"session"`
	Events []eventReq `json:"events"`
}

// importRun は、セーブデータの履歴にある 1 ランを、セッションと記録をまとめて 1 回で受け取る。
func (s *Server) importRun(w http.ResponseWriter, r *http.Request) {
	var in importRunReq
	if err := json.NewDecoder(r.Body).Decode(&in); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body")
		return
	}
	run, msg := buildImportRun(in)
	if msg != "" {
		writeError(w, http.StatusBadRequest, msg)
		return
	}
	same, err := s.Store.ImportRun(r.Context(), run)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to import run")
		return
	}
	if same != nil {
		writeJSON(w, http.StatusConflict, map[string]string{"session_id": same.ID})
		return
	}
	s.warmSummaries(run.Session.ID)
	writeJSON(w, http.StatusCreated, map[string]string{"session_id": run.Session.ID})
}

// buildImportRun は、送られてきたランの形を検査し、書き込む中身にする。形が合わなければ理由を返す。
func buildImportRun(in importRunReq) (store.ImportRunInput, string) {
	ss := in.Session
	started, err := time.Parse(time.RFC3339, ss.StartedAt)
	if err != nil || ss.Seed == "" {
		return store.ImportRunInput{}, "session.seed and session.started_at (RFC3339) are required"
	}
	if len(ss.PlayerIDs) == 0 || len(ss.PlayerIDs) > maxImportPlayers {
		return store.ImportRunInput{}, "session.player_ids must have 1 to 4 players"
	}
	if len(in.Events) == 0 || len(in.Events) > maxImportEvents {
		return store.ImportRunInput{}, "too many events"
	}
	players := map[string]bool{}
	for _, id := range ss.PlayerIDs {
		players[id] = true
	}

	run := store.ImportRunInput{
		Session: store.CreateSessionInput{
			ID: uuid.NewString(), WriteToken: uuid.NewString(), CreatedAt: started,
			HostName: ss.HostName, HostSteamID: ss.HostSteamID, CharacterID: ss.CharacterID,
			Ascension: ss.Ascension, Seed: &ss.Seed, GameVersion: ss.GameVersion,
		},
		PlayerIDs: ss.PlayerIDs,
	}
	floors := 0
	for i, ev := range in.Events {
		if ev.EventUUID == "" || ev.OccurredAt == "" || len(ev.Payload) == 0 {
			return store.ImportRunInput{}, "events[].event_uuid, occurred_at and payload are required"
		}
		last := i == len(in.Events)-1
		switch ev.EventType {
		case "run_start":
			var rs struct {
				PlayerName    string  `json:"player_name"`
				CharacterID   *string `json:"character_id"`
				CharacterName *string `json:"character_name"`
				GameMode      *string `json:"game_mode"`
			}
			if ev.PlayerID == nil || !players[*ev.PlayerID] || json.Unmarshal(ev.Payload, &rs) != nil {
				return store.ImportRunInput{}, "run_start must belong to a player in session.player_ids"
			}
			run.Players = append(run.Players, store.ImportPlayer{
				SteamID: *ev.PlayerID, Name: rs.PlayerName, CharacterID: rs.CharacterID, CharacterName: rs.CharacterName, GameMode: rs.GameMode,
			})
		case "floor_snapshot":
			floors++
			if floors > maxImportFloors {
				return store.ImportRunInput{}, "too many floors"
			}
		case "run_end":
			var re runEndPayload
			if !last || json.Unmarshal(ev.Payload, &re) != nil || (re.Outcome != "victory" && re.Outcome != "death") {
				return store.ImportRunInput{}, "the last event must be run_end with outcome victory or death"
			}
			run.Outcome, run.FinalFloor, run.FinishedAt = re.Outcome, re.FinalFloor, ev.OccurredAt
		default:
			return store.ImportRunInput{}, "only run_start, floor_snapshot and run_end can be imported"
		}
		run.Events = append(run.Events, store.EventInput{
			EventUUID: ev.EventUUID, PlayerID: ev.PlayerID, EventType: ev.EventType,
			OccurredAt: ev.OccurredAt, Floor: ev.Floor, Payload: ev.Payload,
		})
	}
	if run.Outcome == "" {
		return store.ImportRunInput{}, "the last event must be run_end with outcome victory or death"
	}
	return run, ""
}
