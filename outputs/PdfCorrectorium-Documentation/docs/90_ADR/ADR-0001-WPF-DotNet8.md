# ADR-0001 C# / .NET 8 / WPFを採用

- Status: Accepted
- Context: Windows 11 x64向けの高機能ドッキングGUI、画像キャンバス、キーボード操作が必要。
- Decision: C#、.NET 8、WPF、MVVMを採用する。
- Consequences: Windowsへ最適化できる。ARM64はネイティブ依存の対応が必要。UIとCoreを分離する。
