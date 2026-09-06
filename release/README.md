# Windows 配布版

このディレクトリは、ブラウザを使用しない Windows Forms 版を Git だけで取得するための配布物です。

```powershell
git clone --depth 1 https://github.com/zhuyihenzheng/evikit.git
cd evikit\release
Expand-Archive .\evikit-win-x64.zip -DestinationPath .\evikit
& ".\evikit\evikit-win-x64\evikit.exe"
```

`evikit.exe` だけを取り出さず、展開したフォルダー内の全ファイルを保持してください。Bun、Node.js、ブラウザ、WebView2、.NET の追加インストールは不要です。

ハッシュ確認：

```powershell
Get-FileHash .\evikit-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

期待値：`c2757387c60c7c80d486f4d38a61e4b32b5078957b481525326e5e21d2830dce`

状態は alpha です。Windows 実機、RDP、IME、DPI、実 Excel の印刷とリンク操作は未検証です。
