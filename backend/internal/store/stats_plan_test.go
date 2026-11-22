package store

import (
	"strings"
	"testing"
)

// 集計のクエリが、データベース全体のその種類の記録ではなく、そのゲームの記録だけを読むことを確認する。
func TestSummaryQueriesUseSessionTypeIndex(t *testing.T) {
	st, err := Open(":memory:")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = st.Close() })
	for name, q := range map[string]string{"floor_snapshot": sqlFloorSnapshots, "damage_dealt": sqlDamageByPlayer} {
		t.Run(name, func(t *testing.T) {
			rows, err := st.db.Query("EXPLAIN QUERY PLAN "+q, "s")
			if err != nil {
				t.Fatal(err)
			}
			defer rows.Close()
			var plan strings.Builder
			for rows.Next() {
				var id, parent, unused int
				var detail string
				if err := rows.Scan(&id, &parent, &unused, &detail); err != nil {
					t.Fatal(err)
				}
				plan.WriteString(detail + "\n")
			}
			if !strings.Contains(plan.String(), "idx_events_session_type") {
				t.Fatalf("plan:\n%s", plan.String())
			}
		})
	}
}
