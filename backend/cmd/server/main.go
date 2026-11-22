package main

import (
	"context"
	"encoding/hex"
	"errors"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/api"
	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/auth"
	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/steam"
	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
)

func main() {
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: parseLogLevel()}))
	slog.SetDefault(logger)

	addr := ":" + envOr("PORT", "8080")
	dbPath := envOr("DATABASE_PATH", "./data.db")
	baseURL := strings.TrimRight(envOr("BASE_URL", "http://localhost:8080"), "/")
	allowed := splitAndTrim(envOr("CORS_ALLOWED_ORIGINS", "*"))

	st, err := store.Open(dbPath)
	if err != nil {
		slog.Error("failed to open store", "err", err)
		os.Exit(1)
	}
	defer st.Close()

	// ログインの cookie の署名の鍵。DB が漏れても、なりすましてログインできないように DB には置かない
	// (本番は Fly.io の secrets。docs/operations.md「公開サーバへのデプロイ」)
	cookieKey, err := hex.DecodeString(os.Getenv("AUTH_COOKIE_KEY"))
	if err != nil || len(cookieKey) < 32 {
		slog.Error("AUTH_COOKIE_KEY must be at least 32 random bytes in hex (openssl rand -hex 32)")
		os.Exit(1)
	}

	steamKey := os.Getenv("STEAM_WEB_API_KEY")
	if steamKey == "" {
		slog.Error("STEAM_WEB_API_KEY is required (Steam のフレンドに公開したユーザーページの判定に使う)")
		os.Exit(1)
	}

	srv := &api.Server{
		Store:          st,
		BaseURL:        baseURL,
		AllowedOrigins: allowed,
		Signer:         auth.Signer{Key: cookieKey},
		Verify:         auth.VerifyWithSteam(auth.DefaultHTTPClient),
		Friends:        &steam.WebAPI{Key: steamKey, Client: auth.DefaultHTTPClient},
	}

	httpSrv := &http.Server{
		Addr:              addr,
		Handler:           srv.Routes(),
		ReadHeaderTimeout: 10 * time.Second,
	}

	// 集計の作り方を変えた後は、保存してある集計がすべて古くなる。統計のタブを開いた人を待たせないよう、起動直後に作り直す。
	go func() {
		start := time.Now()
		n, err := st.WarmGameSummaries(context.Background(), "")
		if err != nil {
			slog.Error("warm game summaries", "err", err)
			return
		}
		slog.Info("game summaries warmed", "built", n, "elapsed", time.Since(start))
	}()

	go func() {
		slog.Info("server starting", "addr", addr, "db", dbPath, "base_url", baseURL)
		if err := httpSrv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			slog.Error("server failed", "err", err)
			os.Exit(1)
		}
	}()

	stop := make(chan os.Signal, 1)
	signal.Notify(stop, syscall.SIGINT, syscall.SIGTERM)
	<-stop
	slog.Info("shutting down")
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	_ = httpSrv.Shutdown(ctx)
}

func envOr(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func parseLogLevel() slog.Level {
	switch strings.ToLower(os.Getenv("LOG_LEVEL")) {
	case "debug":
		return slog.LevelDebug
	case "warn":
		return slog.LevelWarn
	case "error":
		return slog.LevelError
	default:
		return slog.LevelInfo
	}
}

func splitAndTrim(s string) []string {
	if s == "" {
		return nil
	}
	parts := strings.Split(s, ",")
	out := make([]string, 0, len(parts))
	for _, p := range parts {
		if t := strings.TrimSpace(p); t != "" {
			out = append(out, t)
		}
	}
	return out
}
