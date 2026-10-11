# C# コーディングポリシー（可読性編）

C# の一般的な規約から、読みやすさに効くものを集めて整理した。特定のプロジェクトに固有の決めごとではなく、新しくコードを書く・レビューするときの基準として使う。

## 0. この文書の使い方

### 出典

| 記号 | 出典 | 性格 |
|---|---|---|
| MS | [.NET Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) / [Identifier names](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names) | Microsoft docs チームの規約。runtime と Roslyn の規約を元にしている |
| RT | [dotnet/runtime C# coding style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md) | .NET 本体の規約。MS の元になっている |
| FDG | [Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/naming-guidelines) | 公開 API の命名と設計の指針 |
| G | [Google C# Style Guide](https://google.github.io/styleguide/csharp-style.html) | Google の規約。MS 系と書式が異なる |
| 一般 | 出典なし | 上記の資料に明記はないが、広く共有されている設計上の慣習 |

### 出典同士で割れている点

どれを採るかは先に決めてチームで揃える。この文書では MS / RT 系を既定にしている。

| 項目 | MS / RT | G | この文書の既定 |
|---|---|---|---|
| インデント | 空白 4 | 空白 2 | 4 |
| 波括弧 | Allman（改行して置く） | 同じ行に置く | Allman |
| 波括弧の省略 | 1 行の本体なら省略可（RT） | 常に付ける | 常に付ける（下記 3.3） |
| static フィールドの接頭辞 | `s_` | なし（`_camelCase` のまま） | `s_`（RT） |
| 拡張メソッド | 制限なし | 原則避ける | 使ってよいが、対象型の自然な操作に限る |
| 式形式メンバー | 制限なし | メソッドには使わない | 1 行で収まる読み取り専用プロパティに限る |

## 1. 基本原則

- 明確さと単純さを優先する。凝った書き方より、初めて読む人が迷わない書き方を選ぶ（MS）。
- 省略より明確さを選ぶ。名前は短さより意味が伝わることを優先する（MS）。
- 既存ファイルの書き方がある場合は、それに合わせる。新しい規約を理由に、無関係な行を書き換えない（RT）。
- 新しい言語機能は、読みやすくなる場面で使う。古い構文を意図なく残さない（MS）。
- 規約は書式ツール（`.editorconfig`・アナライザー）で強制し、レビューで人が指摘しない状態にする（MS / RT）。

## 2. 命名

### 2.1 大文字小文字と接頭辞

| 対象 | 形式 | 例 | 出典 |
|---|---|---|---|
| 型（class / struct / record / enum / delegate）、名前空間 | PascalCase | `DataService` | MS |
| interface | `I` + PascalCase | `IWorkerQueue` | MS |
| メソッド、ローカル関数、プロパティ、イベント | PascalCase | `StartProcessing` | MS |
| 定数（private / internal 含む、ローカル定数も） | PascalCase | `MaxRetryCount` | MS / RT |
| 引数、ローカル変数 | camelCase | `workerQueue` | MS |
| private / internal の非定数インスタンスフィールド | `_camelCase` | `_workerQueue` | MS / RT |
| private / internal の static フィールド | `s_camelCase` | `s_instance` | MS / RT |
| `[ThreadStatic]` フィールド | `t_camelCase` | `t_buffer` | MS |
| 型パラメーター | `T` + 説明的な名前。1 つだけなら `T` | `TSession`、`List<T>` | MS |
| クラスの主コンストラクター引数 | camelCase | `DataService(IWorkerQueue workerQueue)` | MS / RT |
| record の位置指定引数 | PascalCase（公開プロパティになるため） | `record Person(string FirstName)` | MS |

- フィールドは `readonly` にできるものを `readonly` にする（RT）。`static readonly` の順に書く。`readonly static` は使わない（RT）。
- 属性型は `Attribute` で終える（MS）。
- 連続するアンダースコア（`__`）を識別子に含めない。コンパイラ生成名と衝突する（MS）。
- 頭字語は単語として扱う。`MyRpc`、`HtmlParser`（G）。2 文字の頭字語を大文字にするかは、既存コードに揃える。

### 2.2 名前の付け方

| 対象 | ルール | 良い例 | 避ける例 | 出典 |
|---|---|---|---|---|
| メソッド | 動詞または動詞句 | `Split`、`CompareTo` | `Data()` | FDG |
| プロパティ | 名詞・名詞句・形容詞。`GetXxx` メソッドと同名にしない（それはメソッドにすべきサイン） | `Count`、`IsEmpty` | `Get` で始まる名前のプロパティ | FDG |
| bool プロパティ・変数 | 肯定形で書く。`Is` / Can / Has は意味が増えるときだけ付ける | `CanSeek` | `CantSeek`、`NotVisible` | FDG |
| コレクション | 複数形にする。`List` / `Collection` を単数形の後ろに付けない | `Windows` | `WindowList` | FDG |
| イベント | 動詞。現在形は発生前・過去形は発生後 | `Closing` / `Closed` | `BeforeClose` / `AfterClose` | FDG |
| イベント引数型 | `EventArgs` で終える | `ClickedEventArgs` | | FDG |
| 同期・非同期 | 非同期メソッドは `Async` で終える（イベントハンドラーを除く） | `LoadAsync` | | MS |
| enum | 通常は単数名詞、`[Flags]` は複数名詞 | `Color` / `FileAttributes` | | MS |

- 変数名に型名を埋め込まない。型は型で示し、名前は意味を示す。`var inputInt = Console.ReadLine();` のような名前は、実際の型と食い違うと誤解を生む（MS）。`iterations` のように意味で付ける。
- 略語・頭字語は広く知られたもの以外は使わない（MS）。
- 1 文字の名前はごく短いループカウンターに限る（MS）。
- 名前空間は逆ドメイン形式で説明的に付ける（MS）。

## 3. 書式

### 3.1 インデントと波括弧

- インデントは空白 4 つ。タブは使わない（MS / RT）。
- 波括弧は Allman 形式。開き・閉じとも単独の行に置き、現在のインデントに揃える（MS / RT）。
- 1 行に文は 1 つ、宣言も 1 つ（MS）。
- 空行は 2 行以上続けない（RT）。メソッドとプロパティの間は 1 行空ける（MS）。
- 不要な空白を入れない（RT）。

### 3.2 長い式の折り返し

- 長い文は複数行に分ける（MS）。
- 折り返す位置は二項演算子の前に置く（MS）。
- 継続行は 1 段（空白 4 つ）インデントする（MS）。
- 条件式は、優先順位を知らなくても読めるよう括弧でまとめを示す（MS）。ただし、優先順位そのものを説明する場面は除く。

```csharp
if ((startX > endX) && (startX > previousX))
{
    // Take appropriate action.
}
```

### 3.3 波括弧の省略

RT は、すべてのブロックの本体が 1 行で収まるときに限って省略を認める。G は常に付ける。省略を許すと、後から行を足したときに if の範囲が変わる事故を招くため、常に付けることを既定にする。

### 3.4 行の長さ

MS の 65 文字は docs の画面表示向けの制限で、一般のコードには当てはまらない。G は 100 文字。実用上は 100〜120 文字で折り返す目安を決め、`.editorconfig` で `max_line_length` を指定する。

## 4. ファイルと型の構成

- 名前空間は file-scoped（`namespace Foo;`）で宣言する（MS）。
- `using` はファイルの先頭、名前空間宣言の外に置く（MS / RT）。名前空間の内側に置くと、依存が増えたとき名前解決が変わり、触っていないコードが壊れることがある（MS）。
- `using` はアルファベット順に並べ、`System.*` を先頭にする（RT / G）。
- 型 1 つにつきファイル 1 つにし、ファイル名は型名に合わせる（G）。
- 可視性は省略せず、修飾子の先頭に書く（RT）。
- フィールドは型宣言の先頭にまとめる（RT）。
- メンバーは次の順に並べる（G）。種類の中では public → internal → protected → private の順にする。
  1. 入れ子の型・enum・delegate・event
  2. 定数・static フィールド
  3. フィールド・プロパティ
  4. コンストラクター・ファイナライザー
  5. メソッド
- インターフェースの実装はまとめて置く（G）。
- 継承する予定のない internal / private な型は `sealed`（または `static`）にする（RT）。
- `this.` は曖昧さを避ける必要があるときだけ付ける（RT）。
- static メンバーは `クラス名.メンバー` で呼ぶ。派生クラス名で基底の static メンバーを修飾しない（MS）。
- 属性は対象の上の行に単独で書く（G）。

## 5. 言語機能の使い方

### 5.1 型の記述と `var`

- `int`・`string` など C# のキーワード型を使い、`Int32`・`String` は使わない（MS / RT）。
- `uint` などの符号なし型より `int` を使う。他のライブラリとの接続が楽になる（MS）。
- `var` は、右辺から型が読み取れるときだけ使う。`new`・明示的キャスト・リテラルが該当する。メソッド名から型が分かるはずという前提は置かない（MS / RT）。
- LINQ のクエリ変数、`for` のループ変数には `var` を使ってよい。`foreach` の要素変数は型を明示する（MS）。
- 変数の型と `new` の型が一致するときは、`var x = new Foo();` または `Foo x = new();` を使う。target-typed `new()` は、型が左辺に書いてあるときに限る（MS / RT）。
- `dynamic` が必要なときに `var` で代用しない（MS）。

### 5.2 文字列

- 短い文字列の連結には文字列補間を使う（MS）。`+` を長く連ねない（G）。
- ループ内で文字列を足していくときは `StringBuilder` を使う（MS）。
- 複数行・エスケープの多い文字列は raw string literal（`"""`）を使う（MS）。
- 補間は式ベース（`$"{name}"`）で書き、位置指定（`{0}`）は使わない（MS）。
- 識別子を文字列で渡す場面は `nameof(...)` を使う（RT）。

### 5.3 コレクションと初期化

- コレクションの初期化はコレクション式（`[ "a", "b" ]`）を使う（MS）。
- 初期化を強制したいプロパティは `required` にする。コンストラクターを増やさずに済む（MS）。
- オブジェクト初期化子は、単純なデータ型で使う。コンストラクターを持つ型では避ける（MS / G）。
- 公開 API の戻り値・引数は、必要最小限のインターフェース型にする（G）。
- 要素の削除をしながら反復しない。`RemoveAll(predicate)` を使うか、新しいコレクションを作って置き換える（G）。

### 5.4 式・条件

- 条件の論理演算は `&&`・`||` を使う。`&`・`|` は短絡評価されない（MS）。
- 単純な読み取り専用プロパティは式形式（`=>`）にしてよい。メソッドを式形式にするかは既存コードに合わせる（G）。
- 3 つ以上の文を持つラムダ、再利用するラムダは名前付きメソッドにする（G）。
- 意味が分からない真偽値やリテラルを引数に渡さない。名前付き引数・enum・名前付き定数にする（G）。
- 魔法数は名前付き定数にする（G）。
- 複数の値を返すときは、複雑なものは名前付きの型にする。タプルを返す場合は要素に名前を付ける（G）。
- `out` は最後の引数に置く。`ref` は入力を書き換える場面に限る（G）。
- デリゲート型を自作せず `Func<>`・`Action<>` を使う（MS）。
- 解除しないイベントハンドラーはラムダで書く（MS）。
- デリゲートは `handler?.Invoke()` で呼ぶ（G）。

### 5.5 LINQ

- クエリ変数には意味のある名前を付ける（MS）。結果の曖昧な名前は付け替える（`CustomerName`・`DistributorName`）（MS）。
- `where` を先に置いて、後続の処理が絞り込み後のデータだけを扱うようにする（MS）。
- 長いチェーンは避ける。1 行で収まらない処理は途中結果に名前を付けて分ける（G）。
- `List<T>.ForEach` に複数文の処理を書かない。`foreach` 文にする（G）。
- LINQ で Task を作るときは `ToArray()` / `ToList()` で即時評価する。遅延評価のままだと、列挙するまで非同期処理が始まらない（MS）。

## 6. メソッドと制御フロー

次の項目は上記の資料に明記がなく、一般の設計慣習として挙げる。

- メソッドは 1 つの目的に絞る。名前を付けにくいときは、責務が混ざっているサインとして扱う（一般）。
- ガード節（引数・前提条件の検査）を先頭に置き、早期に `return` / `throw` する。ネストを深くしない（一般）。
- 目安として、ネストは 3 段まで、メソッドは画面 1 枚に収まる長さにする（一般）。
- 引数は 3〜4 個を超えたら、まとまりのある型にまとめる（一般）。
- `bool` の引数でメソッドの振る舞いを切り替えない。メソッドを分けるか enum にする（一般 / G）。
- 戻り値の `null` で失敗を表現しない。`TryXxx` パターン（`bool` + `out`）か、例外を使う（G）。

## 7. コメント

- コメントは「なぜ」を書く。コードを読めば分かる「何を」は書かない（一般）。
- 公開メンバーには XML ドキュメントコメントを付ける（MS）。
- 短い説明は `//` を使い、`/* */` で長い説明を書かない。長い説明はドキュメントに置く（MS）。
- コメントは対象の上の行に単独で置き、行末に付けない（MS）。
- `//` の後ろに空白を 1 つ入れる（MS）。
- 使われなくなったコードはコメントアウトして残さず、削除する。履歴はバージョン管理が持つ（一般）。

## 8. 非同期処理

- I/O 待ちは `async` / `await` を使う。CPU 負荷の高い処理は `Task.Run` で別スレッドに出す（MS）。
- 非同期メソッドは `Async` で終える（MS）。
- `async void` はイベントハンドラー以外で使わない。例外が呼び出し元で捕捉できず、テストもしにくい（MS）。
- `Task.Wait()`・`Task.Result` でブロックしない。デッドロックと `AggregateException` による例外のラップを招く。やむを得ない場合は `GetAwaiter().GetResult()` を使う（MS）。
- `await` を含まない `async` メソッドを作らない（MS）。
- `ConfigureAwait` の指定を誤ると、性能低下やデッドロックを招く。意図を理解したうえで指定する（MS）。
- グローバルな状態に依存せず、戻り値だけに依存する書き方にすると、非同期処理も読みやすく、テストしやすくなる（MS）。

## 9. 例外とリソース

- 処理できる例外だけを捕捉する。`System.Exception` を、例外フィルターなしで捕捉しない（MS）。
- 例外型は具体的なものを使い、意味のあるメッセージを付ける（MS）。
- 捕捉した例外を再スローするときは `throw;` を使う（MS の例）。`throw ex;` はスタックトレースを書き換えるので使わない（一般）。
- `try`-`finally` で `Dispose` だけを呼ぶ場合は `using` を使う。波括弧のない `using` 宣言（`using var x = ...;`）が使えるなら、それを使う（MS）。

## 10. ツールで強制する

- `.editorconfig` に命名規則・書式・`using` の並びを書き、IDE とビルドの両方で効かせる（MS / RT）。出発点には [dotnet/docs の `.editorconfig`](https://github.com/dotnet/docs/blob/main/.editorconfig) を使える（MS）。
- `dotnet format` で書式を揃え、CI で差分があれば失敗させる（RT）。
- アナライザー（.NET analyzers の CA ルール）で、型パラメーター名（CA1715）などを検査する（MS）。
- ビルドの警告は放置せず、ゼロを保つ（一般）。

## 11. チェックリスト（レビュー用）

- [ ] 名前だけで役割が分かる。型名・略語に頼っていない
- [ ] メソッドが 1 つのことをしていて、ネストが浅い
- [ ] `var` の右辺から型が読み取れる
- [ ] 魔法数・意味の不明な `bool` 引数がない
- [ ] コメントが「なぜ」を説明している
- [ ] 非同期メソッドが `Async` で終わり、`.Result` / `.Wait()` を使っていない
- [ ] 例外を握りつぶしておらず、`throw;` で再スローしている
- [ ] `.editorconfig` の書式と命名に従っている
