# AGENTS.md

## MSIX パッケージ（installer/）

`installer/build.ps1` が `dotnet publish` → layout 組み立て → `makeappx pack` → `signtool sign` を行う。Visual Studio の Packaging Project は使っていない。

### はまりポイント

- **Publisher と証明書の Subject は完全一致させる。** `AppxManifest.xml` の `Identity/@Publisher`（`CN=Dockyard Dev`）と署名証明書の Subject が 1 文字でも違うと `signtool` が失敗する。どちらかを変えるときは両方変える。
- **未署名の .msix はインストールできない。** 自己署名証明書は `Cert:\CurrentUser\My` に作って署名に使うが、インストールする側では `Cert:\LocalMachine\TrustedPeople` に入れて信頼させる必要がある（管理者権限が要る）。
- **`runFullTrust` と `Windows.FullTrustApplication` は外せない。** Dockyard は他のアプリのウィンドウを `SetWindowPos` で動かし WinEvent をフックするので、サンドボックス（AppContainer）では動かない。
- **.NET ランタイムへの依存はマニフェストに書けない。** framework-dependent で publish しているので、.NET 10 Desktop Runtime がない PC では起動時に .NET 側のダイアログが出るだけになる。
- **AppData への書き込みはパッケージ専用の場所へリダイレクトされる。** パッケージから起動した Dockyard の `Log.cs` のログは `%LOCALAPPDATA%\Dockyard\` ではなく `%LOCALAPPDATA%\Packages\Dockyard_<hash>\LocalCache\Local\Dockyard\` に出る。
- **ロゴは PNG が必須で、ico からは自前で切り出す。** `System.Drawing.Icon(path, 256, 256)` は ico 内の 256px（PNG 圧縮のエントリ）を返さず 128px になる。`build.ps1` は ico のディレクトリを読み、幅 0（= 256px）のエントリの PNG バイト列を直接取り出している。
- **`makeappx` / `signtool` は PATH に無い。** Windows SDK の `C:\Program Files (x86)\Windows Kits\10\bin\<ver>\x64\` にある。`build.ps1` は最新バージョンを探して使う。
- **自動起動は `windows.startupTask`（TaskId `DockyardStartup`）。** `Autostart.cs` の TaskId と一致させる。既定は無効で、設定画面かタスク マネージャーのスタートアップ アプリでオンにする。WinRT の `StartupTask` を使うため App の TFM は `net10.0-windows10.0.19041.0`。パッケージ外で起動したときは HKCU の Run キーを使う。
- **バージョンは 4 つ組で、更新するときは上げる。** 同じ Identity で中身だけ違うパッケージは上書きインストールできない。`-Version 1.0.1.0` のように指定する。

### 開発中の登録（`build.ps1 -Dev`）

- 開発者モードが有効なら、署名せずに layout フォルダをそのまま登録できる（`Add-AppxPackage -Register`）。
- 登録したパッケージは layout フォルダのファイルをその場で使う。フォルダを消すと壊れるので、`-Dev` は作り直す前に `Remove-AppxPackage` で登録を外している。パッケージ版の Dockyard を起動したままだとファイルがロックされて失敗するので、先に終了する。
- 開発登録と署名付き .msix は同じ Identity なので同時には入らない。切り替えるときは `Get-AppxPackage Dockyard | Remove-AppxPackage` で外してから入れる。
