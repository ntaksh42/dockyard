# Dockyard

別々のアプリのウィンドウを 1 つの枠にまとめ、タブと分割ペインでドッキング配置する Windows 常駐アプリ。仕様のすり合わせ結果は [docs/mock.html](docs/mock.html)、設計は [docs/architecture.md](docs/architecture.md)。

## 実行

```
dotnet run --project src/Dockyard.Exe
dotnet test
```

起動するとタスクトレイに常駐し、空のグループ枠が 1 つ開く。トレイアイコンのダブルクリックで新しいグループを作る。

タイトルバーの自動非表示（既定でオン）。グループ上端の 4px、または各ペインの内容領域の上端にマウスを置くと表示し、離れてしばらく（既定 400ms）たつと隠す。設定画面の「Title bars」でオン・オフと隠すまでの時間を変えられ、オン・オフは開いている全グループに適用する。グループのタイトル部分を右クリックし「Auto-hide Title Bars」でそのグループだけ切り替えられ、その状態はレイアウトの保存・復元とセッションにも含まれる。ブラウザや VS Code など、設定のタイトルバー例外一覧にあるアプリは残す。Files の WinUI ウィンドウや Orca の Chromium/Electron ウィンドウも、独自ヘッダーの描画が崩れないよう標準タイトルバーの変更を適用しない。外部アプリは標準タイトルバーが対象で、切り替え時には内容領域の高さが変わる。グループ解除・正常終了時に元に戻し、強制終了で残った非表示状態は次回起動時に復元する。

## インストーラー（MSIX）

```
pwsh installer/build.ps1
```

`artifacts/msix/Dockyard.msix` と、署名に使った自己署名証明書 `Dockyard.cer` ができる。パッケージはビルドした PC のアーキテクチャ（arm64 か x64）向けになる。別のアーキテクチャ向けは `-Runtime x64` / `-Runtime arm64` で指定する（x64 版も ARM64 の PC に入るが、エミュレーションで動くので遅く重い）。初回だけ、管理者の PowerShell で証明書を信頼させてからインストールする。

```
Import-Certificate -FilePath artifacts\msix\Dockyard.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage artifacts\msix\Dockyard.msix
```

実行には、パッケージと同じアーキテクチャの .NET 10 Desktop Runtime が別途必要。

アンインストールは `Get-AppxPackage Dockyard | Remove-AppxPackage`。Dockyard を終了してから行う（終了時にグループのウィンドウを元の位置に戻すため）。信頼させた証明書は、管理者の PowerShell で `Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=Dockyard Dev' | Remove-Item` で消せる。

本番用の証明書で署名するときは、`Cert:\CurrentUser\My` にある証明書を `-CertificateThumbprint` で指定する（マニフェストの Publisher は証明書の Subject になる）。`-TimestampUrl` に RFC 3161 のタイムスタンプサーバーを渡すと、証明書の期限が切れても署名が有効なままになる。

`pwsh installer/test.ps1` は、ビルドしたパッケージを実際にインストールし、起動・上書き更新・アンインストールまで確かめる（証明書を信頼させてあり、Dockyard のパッケージが入っていないこと）。

リリースは GitHub Actions が毎朝 6:00（JST）に、前回から `src/`・`installer/` に変更があれば自動で作る（`.github/workflows/release.yml`、詳細は [AGENTS.md](AGENTS.md)）。

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
- 設定画面（トレイメニュー、タブバーの歯車ボタン、またはタブバーの「+」→「Settings…」）でタブバーの位置を切り替える（Q2）。A: ウィンドウの上（既定）、B: ウィンドウのタイトルバーに重ねる、C: 左に縦タブ。B でも、タイトルバーにタブやメニューを持つアプリ（既定は chrome / msedge / firefox / brave / opera / vivaldi / Code / WindowsTerminal / explorer、設定画面で変更可）を含むペインは A の位置にタブを出す。設定は `%LOCALAPPDATA%\Dockyard\settings.json`。キャプションの右クリックメニューの「Tab Bar」でグループごとに変えられ、その配置はレイアウトの保存・復元にも含まれる
- レイアウトの保存と復元（Q3）。枠のキャプションの保存ボタンで、グループ名をキーに分割の構成と各タブのアプリ（exe 名・タイトル）を `%LOCALAPPDATA%\Dockyard\layouts.json` に保存する。トレイメニューの「Open Layout」で新しいグループとして開く。タブバーの「+」やキャプションの右クリックメニューの「Open Layout」では、ウィンドウの無いグループならそのグループに読み込む。いま開いているウィンドウを、同じ exe と同じタイトル → 同じ exe の順に割り当てる（ウィンドウハンドルと追加前の位置は保存しない。ハンドルは使い回され、位置はそのときのものだから）。見つからないタブは「Waiting for: exe — タイトル」と表示して待ち、その exe のウィンドウが新しく開いたら（同じタイトルを優先して）そのペインに入れる。各タブの exe のパスも保存し、設定で「When opening a layout, start apps that are not open」をオンにすると、開くときに足りないアプリを起動する（起動したウィンドウは待機中のタブに入る）。同じ名前で保存すると上書きし、その旨を通知する。トレイメニューの「Layouts…」（Ctrl+Alt+L）で設定画面の Layouts タブを開き、保存済みレイアウトを開く・名前変更・削除できる
- レイアウトのホットキーとデスクトップのショートカット（PowerToys の Workspaces のように、保存したレイアウトをワンアクションで起動する）。設定画面の Layouts タブ（トレイの「Layouts…」か Ctrl+Alt+L）の表で、レイアウトの行にある「Click, then press keys」の欄を押し、Ctrl / Alt / Win のいずれかを含むキーを押すと割り当てる（✕ で外す）。レイアウトを保存した直後の通知をクリックしても、そのレイアウトを選んだ状態で Layouts タブが開く。ホットキーはフォーカスがどこにあっても効き、そのレイアウトを開いて、足りないアプリを（設定のチェックボックスに関係なく）起動する。同じ名前のグループがすでに開いていれば、開き直さずに前面へ出す（連打でアプリが二重に起動しないように）。割り当てたキーは、同じキーを持つ他のレイアウトやショートカット（設定画面）から外れる。行の「Desktop shortcut」は `Dockyard - <名前>.lnk` をデスクトップに作る。中身は `Dockyard.exe --layout "<名前>"` で、Dockyard が動いていればそこへ渡して 2 つ目はすぐ終了し、動いていなければ起動してそのレイアウトを開く（空のグループは作らない）。MSIX 版のショートカットはパッケージの実行エイリアス `dockyard.exe` を指す
- 同時に動く Dockyard は 1 つだけ。2 つ目を起動してもすぐ終了する（同じウィンドウや保存ファイルを取り合わないため）
- Dockyard が強制終了・異常終了したあとの起動では、前回のグループを同じウィンドウで組み直す（`session.json`）。閉じれば各ウィンドウは追加前の位置に戻る
- 設定の「Start Dockyard when Windows starts」で Windows 起動時に自動で起動する（MSIX 版はスタートアップ タスク、それ以外は HKCU の Run キー）
- 設定の「When Dockyard starts, open the groups from last time」をオンにすると、起動時に前回終了時のグループ（`last-groups.json`）をレイアウトと同じ規則（exe・タイトル）で開く。PC の再起動後でも使える
- 枠のグループ名をダブルクリックして名前を変更（Enter で確定、Esc で取り消し）。枠のキャプションの右クリックメニューからも、名前の変更・レイアウトの保存・設定を開ける
- キーボードショートカット（Q7）：Ctrl+Alt+←/→ でタブ切替、Ctrl+Alt+1〜8 でその番号のタブ、Ctrl+Alt+9 で最後のタブ、Ctrl+Alt+Shift+矢印で隣接ペインへ移動、Ctrl+Alt+Win+矢印でタブを隣のペインへ移す（隣が無ければその向きに分割）、Ctrl+Alt+] / [ でペインを広げる・狭める、Ctrl+Alt+W でグループから解除。枠の操作は Ctrl+Alt+R で名前の変更、Ctrl+Alt+B でタブバーの位置を切り替え、Ctrl+Alt+↑ で最大化・元に戻す、Ctrl+Alt+↓ で最小化、Ctrl+Alt+Shift+W でグループを閉じる、Ctrl+Alt+M で「+」メニュー（ウィンドウの追加・分割・レイアウトを開く・設定）、Ctrl+Alt+T でウィンドウをタブとして追加。グループ内のウィンドウまたは枠にフォーカスがあるときのみ有効。設定画面で割り当てを変更できる（Ctrl / Alt / Win のいずれかを含む組み合わせ）。いまの割り当てはタブバーの「+」→「Keyboard Shortcuts」で一覧できる

