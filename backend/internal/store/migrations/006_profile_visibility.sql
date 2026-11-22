-- マイページの公開範囲を 3 段階にする: public (全体) / friends (Steam のフレンド) / private (docs/spec/user-pages.md)。
ALTER TABLE players ADD COLUMN profile_visibility TEXT NOT NULL DEFAULT 'private';
UPDATE players SET profile_visibility = 'public' WHERE profile_public = 1;
ALTER TABLE players DROP COLUMN profile_public;
