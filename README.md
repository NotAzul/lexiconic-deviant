# lexiconic-deviant
like 'find &amp; replace', but 'define &amp; rephrase'; a semantic thesaurus for the insufficiently think-motivated

## FenceForge

FenceForge is a Windows WPF clipboard transformation utility. Its source is in [`FenceForge/`](FenceForge/).

Build it on Windows with the .NET 10 SDK:

```powershell
dotnet build .\FenceForge\FenceForge.csproj --configuration Release
dotnet publish .\FenceForge\FenceForge.csproj --configuration Release -p:PublishProfile=win-x64
```

The published executable is written to `FenceForge\publish\win-x64\FenceForge.exe`. FenceForge stores its Gemini API key in Windows Credential Manager under `FenceForge/GeminiApiKey`; the key is not part of this repository.
