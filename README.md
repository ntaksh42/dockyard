# Dockyard

別々のアプリのウィンドウを 1 つの枠にまとめ、タブと分割ペインでドッキング配置する Windows 常駐アプリ。仕様のすり合わせ結果は [docs/mock.html](docs/mock.html)、設計は [docs/architecture.md](docs/architecture.md)。

## 実行

```
dotnet run --project src/Dockyard.App
dotnet test
```

起動するとタスクトレイに常駐し、空のグループ枠が 1 つ開く。トレイアイコンのダブルクリックで新しいグループを作る。

## インストーラー（MSIX）

```
pwsh installer/build.ps1
```

`artifacts/msix/Dockyard.msix` と、署名に使った自己署名証明書 `Dockyard.cer` ができる。初回だけ、管理者の PowerShell で証明書を信頼させてからインストールする。

```
Import-Certificate -FilePath artifacts\msix\Dockyard.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage artifacts\msix\Dockyard.msix
```

実行には .NET 10 Desktop Runtime が別途必要。

開発中は `pwsh installer/build.ps1 -Dev` で、署名せずに layout フォルダをそのまま登録できる（開発者モードが必要）。はまりポイントは [AGENTS.md](AGENTS.md)。

## 実装済み

- グループ枠（自前のウィンドウ）とレイアウトツリー（縦横分割 + タブ）
- ペインの「+」から既存ウィンドウを選び、タブとして追加、または右・下に分割して追加
- Shift + 外部ウィンドウのタイトルバーをドラッグして枠へ追加（独自タイトルバーのアプリにも対応）。中央・タブバーでタブ追加、上下左右の端で分割（ガイドと半透明プレビューを表示）
- タブをドラッグしてペイン間・グループ間で再配置。枠外へドロップすると解除、Esc でキャンセル
- 外部ウィンドウは reparent せず、ペインの位置へ追従させる（Q8）。非アクティブなタブは枠の裏に置くので、Alt+Tab などの切替ツールには出たまま（Q11）
- 枠の移動・リサイズ・最小化・復元への追従
- メンバーウィンドウの端をドラッグすると、その辺の仕切り線が動く。外周の辺なら枠がリサイズされる
- アプリの最小サイズを観測し、そこで仕切り線を止める（Q9）
- タブの × またはグループを閉じると、ウィンドウを追加前の位置とサイズに戻す

## 未実装

- レイアウトの保存と復元（Q3）
- タブバー配置の設定 B / C（Q2。現在は A のみ）
- キーボードショートカット（Q7）
- グループ名の変更
