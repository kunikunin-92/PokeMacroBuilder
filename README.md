# PokeMacro Builder

**Poke-Controller のマクロを、Scratch のようなブロックを並べるだけで作れる Windows アプリです。**

Python を書かなくても、「A ボタンを押す」「3 秒待つ」「10 回くりかえす」といったブロックを
ドラッグして組み立てるだけで、そのまま Poke-Controller から実行できる `.py` ファイルが出来上がります。

- 🧩 **コードを書かない** — ブロックを置いて数値を入れるだけ
- 👀 **その場でコードが見える** — 右側に生成される Python がリアルタイム表示される
- 🔁 **あとから直せる** — 作ったマクロはブロックのまま何度でも読み込んで編集できる
- 🖼 **画像認識・文字認識にも対応** — 「この画像が出たら」「この文字が出たら」で分岐できる

---

## 動作環境

| | |
|---|---|
| OS | Windows 10 / 11（64bit） |
| 必要なもの | [Poke-Controller](https://github.com/KawaSwitch/Poke-Controller)（または Poke-Controller Modified） |
| .NET | **インストール不要**（配布版に同梱されています） |

画像認識・文字認識を使う場合のみ、追加で以下が必要です（使わなければ不要）。

- **画像認識** … Poke-Controller 側でキャプチャボード（カメラ）が使えること
- **文字認識(OCR)** … [Tesseract-OCR](https://github.com/UB-Mannheim/tesseract/wiki) と、Poke-Controller の Python 環境に `pytesseract`

---

## インストール

1. [Releases](https://github.com/kunikunin-92/PokeMacroBuilder/releases/latest) から
   `PokeMacroBuilder-vX.X.X-win-x64.zip` をダウンロードします。
2. zip を**展開**します（展開せずに中身を直接実行しないでください）。
3. `PokeMacroBuilder.exe` をダブルクリックで起動します。

インストーラーはありません。フォルダごと好きな場所に置いて使えます。
不要になったらフォルダを削除するだけでアンインストール完了です。

> **「Windows によって PC が保護されました」と出たら**
> 配布ファイルにデジタル署名を付けていないため、初回起動時に SmartScreen の警告が出ます。
> 「詳細情報」→「実行」で起動できます。

設定（前回開いたフォルダ・テーマなど）は `%AppData%\PokeMacroBuilder\settings.json` に保存されます。

---

## 使い方

### 1. ワークスペースを選ぶ

起動したら **「フォルダを選択...」** で Poke-Controller のフォルダを指定します。

ルートフォルダでも `SerialController` でも、配下に `PythonCommands` があれば自動で見つけます。
一度選べば次回から自動で開きます。

### 2. マクロを作る

**「新規作成」** → 表示名を入力すると、ブロック編集画面になります。

左の**ツールボックス**からブロックをドラッグして真ん中に置いていくだけです。
（クリックすると末尾に追加されます）

| ブロック | できること |
|---|---|
| 🎮 **ボタン** | A / B / X / Y / L / R… を押す。押す時間・待機・連打回数を指定。複数キーを足せば**同時押し**、モードを変えれば**押しっぱなし / 離す**にもなる |
| 🕹 **スティック** | 左右スティック・十字キーを 8 方向に倒す。倒す強さ（%）も指定可能 |
| ❓ **条件分岐** | もし〜なら。**でなければもし(elif)** と **でなければ(else)** も追加できる |
| 🔁 **ループ** | ずっと / 指定回数 / 条件が成り立つ間 |
| ⏱ **待機** | 指定秒数だけ待つ |
| 🔖 **変数** | カウンタなどの代入・加算（`=` `+=` `++` など） |
| 📝 **ログ出力** | Poke-Controller のログ欄にメッセージを出す |
| 📢 **Discord通知** | Discord にメッセージを送る。本文に `{cnt}` と書くと変数の値が入る。スクショ添付も可 |
| 📷 **スクショ保存** | 画面をキャプチャして保存する |

ブロックは掴んで並べ替えたり、条件分岐やループの**中に入れ子**にしたりできます。
`Ctrl+Z` / `Ctrl+Y` で元に戻す・やり直しもできます。

### 3. 保存する

**ファイル → 上書き保存**（`Ctrl+S`）で、Poke-Controller のコマンドフォルダに保存されます。

```
<Poke-Controller>/SerialController/Commands/PythonCommands/MacroBuilder/macro1.py
```

あとは Poke-Controller 側で **コマンドを再読み込み**すれば、入力した表示名がリストに現れます。

### 4. あとから編集する

ホーム画面のマクロ一覧をダブルクリックすると、ブロックの状態そのままで開き直せます。
（生成した `.py` の先頭コメントに編集情報を埋め込んでいるためです）

### ショートカット

| キー | 動作 |
|---|---|
| `Ctrl+N` | 新規マクロ |
| `Ctrl+O` | ワークスペースを開く |
| `Ctrl+S` | 上書き保存 |
| `Ctrl+Z` / `Ctrl+Y` | 元に戻す / やり直し |

---

## 画像認識を使う

条件分岐やループの条件で **「画像」** を選ぶと、「画面にこの画像が写っていたら」で分岐できます。

1. マクロを一度**保存**します（画像はマクロ単位で管理されるため）。
2. 画面中央の**テンプレ画像**の欄に、判定したい画像を追加します。
   - **「➕ 画像を追加」** から選ぶか、画像ファイルを**ドラッグ&ドロップ**します。
   - Poke-Controller のスクショから選ぶなら **「📷 Captureから取り込み」** が便利です。
   - 追加するときに切り抜き（トリミング）ができます。
3. 条件で画像と**一致率**（既定 0.8）を選びます。

画像は次の場所に保存されます。

```
<Poke-Controller>/SerialController/Template/macro1/img1.png
```

> 画像認識・スクショを使うマクロは、自動的に `ImageProcPythonCommand` として生成されます。
> 実行にはキャプチャボード（カメラ）が必要です。

## 文字認識(OCR)を使う

「画面のこの範囲にこの文字が出たら」で分岐できます。事前準備が必要です。

1. [Tesseract-OCR](https://github.com/UB-Mannheim/tesseract/wiki) をインストールします
   （日本語を読むなら言語データ `jpn` も入れてください）。
2. Poke-Controller の Python 環境に `pytesseract` を入れます。

   ```bash
   pip install pytesseract
   ```

3. 本アプリの **ファイル → 設定** で `tesseract.exe` の場所を指定します。
   ✓ が出れば準備完了です。

あとは条件で **「文字(OCR)」** を選び、読み取る文字・**完全一致/含む**・言語（日本語/英語）・
読み取る範囲（座標）を指定します。範囲を 0 のままにすると画面全体が対象です。

---

## よくある質問

**Q. 作ったマクロが Poke-Controller に出てこない**
Poke-Controller 側でコマンドの再読み込み（再起動）をしてください。
それでも出ない場合は、ワークスペースが目的の Poke-Controller のフォルダになっているか確認してください。

**Q. 生成された .py を手で編集してもいい？**
自由に編集できますが、先頭の `# PMB-DATA:` の行は編集情報なので**書き換えないでください**。
この行を消すと、本アプリの一覧に出てこなくなり、ブロックとして読み込めなくなります。

**Q. 手書きの既存マクロも編集できる？**
いいえ。本アプリの一覧に出るのは、本アプリで作成したマクロだけです。

**Q. Discord 通知が飛ばない**
Poke-Controller 側の Discord 連携設定（Webhook）が必要です。本アプリはその機能を呼び出すだけです。

---

## 開発者向け：ソースからビルドする

- Visual Studio 2022 / .NET 8 SDK（WPF, C#）

```bash
git clone https://github.com/kunikunin-92/PokeMacroBuilder.git
cd PokeMacroBuilder
dotnet build PokeMacroBuilder/PokeMacroBuilder.csproj -c Release
```

`PokeMacroBuilder.sln` を Visual Studio で開いて F5 でも実行できます。

配布用（.NET ランタイム同梱の単一 exe）を作る場合：

```bash
dotnet publish PokeMacroBuilder/PokeMacroBuilder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

---

## ライセンス

[MIT License](LICENSE) — 著作権表示を残していただければ、自由に使用・改変・再配布できます。

## 免責

本ツールは Poke-Controller の非公式な補助ツールです。
本ソフトウェアは無保証で提供されます。利用は自己責任でお願いします。
