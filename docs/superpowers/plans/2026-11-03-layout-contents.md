# 保存レイアウト内容の表示 実装計画

**目的:** 合意済みの `review/layout-contents.html` の推奨案で、開く前にレイアウトの中身を確認できるようにする。

**構成:** Application の `LayoutDescription.Describe(SavedNode)` が要約と詳細を返す。設定画面と WPF / Windows Forms メニューが共用する。保存形式は変えない。

**技術:** C# / .NET 10、WPF、Windows Forms、xUnit。

- [x] 共通処理のテストを追加し、未実装での失敗を確認する。
  - アプリ名は大文字小文字を区別せず重複を除き、タブ数は全件を数える。
  - 空ペインも数え、単数・複数を表示する。
  - 2 ペインは Left/Right または Top/Bottom、その他は Pane 1… とする。
  - 詳細には全タブの保存時タイトルを表示する。
- [x] 共通処理と公開 API 一覧を追加し、テストを通す。
- [x] 設定画面の名前の下に要約を追加し、設定画面と両種メニューのツールチップに詳細を表示する。
- [x] 全体のビルドとテスト、差分チェックを実施する。実機 E2E は事前確認が必要なため実行しない。

作業は既存の専用 worktree で直接実施する。

## 検証結果

- 共通処理の4件のテスト: 空の実装で4件失敗 → 実装後に4件成功。
- `dotnet build Dockyard.slnx --verbosity quiet`: 警告0、エラー0。
- `dotnet test Dockyard.slnx --no-build --verbosity quiet`: Application 376件、Domain 223件、Architecture 18件、全617件成功。
- `git diff --check`: 出力なし、終了コード0。
- 実機の表示・操作および実機 E2E は未確認。
