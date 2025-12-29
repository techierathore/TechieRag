# TechieRag NuGet Publishing Guide

This guide covers how to publish TechieRag as a NuGet package for use in other applications.

---

## Table of Contents

1. [Preparing the Package](#preparing-the-package)
2. [Publishing to NuGet.org (Public)](#publishing-to-nugetorg-public)
3. [Publishing to Private Feeds](#publishing-to-private-feeds)
4. [Free/Cheap Hosting Options](#freecheap-hosting-options)
5. [Consuming the Package](#consuming-the-package)

---

## Preparing the Package

### Step 1: Update Project Metadata

Edit `src/TechieRag/TechieRag.csproj` to include all necessary package metadata:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <!-- Package Metadata -->
    <PackageId>TechieRag</PackageId>
    <Version>1.0.0</Version>
    <Authors>Techie Rathor</Authors>
    <Company>Your Company Name</Company>
    <Description>Configurable RAG (Retrieval-Augmented Generation) library for .NET. Supports multiple vector stores (SQLite-vec, PGVector, Qdrant) and embedding providers (Ollama, LM Studio, Azure OpenAI).</Description>
    <PackageTags>RAG;AI;Embeddings;VectorDB;LLM;NLP;SemanticSearch;Ollama;OpenAI</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/yourusername/TechieRag</PackageProjectUrl>
    <RepositoryUrl>https://github.com/yourusername/TechieRag.git</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>icon.png</PackageIcon>

    <!-- Build Settings -->
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>

  <!-- Include README and Icon in package -->
  <ItemGroup>
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
    <None Include="..\..\icon.png" Pack="true" PackagePath="\" Condition="Exists('..\..\icon.png')" />
  </ItemGroup>

  <!-- Dependencies -->
  <ItemGroup>
    <!-- ... existing package references ... -->
  </ItemGroup>

</Project>
```

### Step 2: Create Package Files

Create a `README.md` in the project root (if not exists):

```markdown
# TechieRag

A configurable RAG library for .NET 10.

## Quick Start

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .Build();

await rag.InitializeAsync();
await rag.IngestAsync("document.pdf");
var results = await rag.SearchAsync("your query", topK: 5);
```

## Features

- Multiple vector stores: SQLite-vec, PGVector, Qdrant
- Multiple embedding providers: Ollama, LM Studio, Azure OpenAI
- Document processors: PDF, DOCX, TXT, MD, HTML, JSON, TOML, Code
```

### Step 3: Build the Package

```powershell
cd C:\3AIGenCode\TechieRag

# Build in Release mode
dotnet build src/TechieRag/TechieRag.csproj -c Release

# Create the NuGet package
dotnet pack src/TechieRag/TechieRag.csproj -c Release -o ./nupkg

# This creates:
# ./nupkg/TechieRag.1.0.0.nupkg
# ./nupkg/TechieRag.1.0.0.snupkg (symbols)
```

---

## Publishing to NuGet.org (Public)

NuGet.org is free and the standard public repository for .NET packages.

### Step 1: Create NuGet.org Account

1. Go to https://www.nuget.org/
2. Click "Sign in" → "Register"
3. Create account (Microsoft account or username/password)

### Step 2: Get API Key

1. Go to https://www.nuget.org/account/apikeys
2. Click "Create"
3. Name: "TechieRag Publishing"
4. Expiration: 365 days
5. Glob Pattern: `TechieRag*`
6. Scopes: "Push new packages and package versions"
7. Click "Create" and copy the key

### Step 3: Publish

```powershell
# Set your API key (do this once)
dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json

# Or store the key securely
dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org -u yourusername -p YOUR_API_KEY --store-password-in-clear-text

# Then push without specifying key
dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --source nuget.org
```

### Cost: FREE

---

## Publishing to Private Feeds

### Option 1: Azure Artifacts (Recommended for Teams)

**Cost:** Free for up to 2 GB storage with Azure DevOps Basic plan

#### Setup:

1. Go to Azure DevOps → Artifacts → Create Feed
2. Name: "TechieRag-Private"
3. Visibility: "Members of [organization]"

#### Publish:

```powershell
# Add the feed
dotnet nuget add source https://pkgs.dev.azure.com/YOUR_ORG/_packaging/TechieRag-Private/nuget/v3/index.json -n AzureArtifacts -u YOUR_EMAIL -p YOUR_PAT

# Push package
dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --source AzureArtifacts
```

---

### Option 2: GitHub Packages (Free for Public/Private Repos)

**Cost:** Free with GitHub account (500MB for free tier, unlimited for Pro/Teams)

#### Setup:

1. Go to GitHub → Settings → Developer settings → Personal access tokens
2. Generate new token with `write:packages` scope

#### Configure nuget.config:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/YOUR_USERNAME/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="YOUR_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_GITHUB_TOKEN" />
    </github>
  </packageSourceCredentials>
</configuration>
```

#### Publish:

```powershell
dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --source github
```

---

### Option 3: GitLab Package Registry (Free)

**Cost:** Free with GitLab account

#### Publish:

```powershell
dotnet nuget add source https://gitlab.com/api/v4/projects/PROJECT_ID/packages/nuget/index.json -n gitlab -u YOUR_USERNAME -p YOUR_TOKEN

dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --source gitlab
```

---

### Option 4: Local/Network Share (Simplest for Small Teams)

**Cost:** Free (just needs a shared folder)

#### Setup:

```powershell
# Create a folder on a network share
mkdir \\server\packages\nuget

# Add as source
dotnet nuget add source \\server\packages\nuget -n LocalFeed
```

#### Publish:

```powershell
# Simply copy the package
copy ./nupkg/TechieRag.1.0.0.nupkg \\server\packages\nuget\
```

---

### Option 5: BaGet (Self-Hosted, Free & Open Source)

**Cost:** Free (host on your own server or cloud VM)

BaGet is a lightweight NuGet server you can self-host.

#### Deploy with Docker:

```bash
docker run -d -p 5000:80 --name baget -v baget-data:/var/baget loicsharma/baget
```

#### Configure:

```powershell
dotnet nuget add source http://localhost:5000/v3/index.json -n BaGet

dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --source BaGet --api-key YOUR_KEY
```

---

### Option 6: Cloudsmith (Free Tier Available)

**Cost:** Free for open source, $9/month for private

#### Setup:

1. Create account at https://cloudsmith.com
2. Create repository

#### Publish:

```powershell
dotnet nuget add source https://nuget.cloudsmith.io/YOUR_ORG/YOUR_REPO/v3/index.json -n cloudsmith -u YOUR_EMAIL -p YOUR_API_KEY

dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --source cloudsmith
```

---

## Free/Cheap Hosting Options Summary

| Provider | Free Tier | Private Feeds | Best For |
|----------|-----------|---------------|----------|
| **NuGet.org** | Unlimited (public) | No | Open source packages |
| **GitHub Packages** | 500MB | Yes | GitHub users, small teams |
| **Azure Artifacts** | 2GB | Yes | Azure DevOps teams |
| **GitLab Package Registry** | 5GB | Yes | GitLab users |
| **Local/Network Share** | Unlimited | Yes | Small internal teams |
| **BaGet (Self-hosted)** | Unlimited | Yes | Full control, any size |
| **Cloudsmith** | Open source free | $9/month | Professional teams |
| **MyGet** | 500MB | $9/month | CI/CD integration |

### My Recommendations:

1. **For Personal/Learning:** GitHub Packages (free, easy setup)
2. **For Small Team:** Local network share or BaGet
3. **For Enterprise:** Azure Artifacts or self-hosted BaGet
4. **For Open Source:** NuGet.org (it's the standard)

---

## Consuming the Package

### From NuGet.org

```powershell
dotnet add package TechieRag
```

### From Private Feed

Add to your project's `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="TechieRag-Private" value="YOUR_FEED_URL" />
  </packageSources>
</configuration>
```

Then:

```powershell
dotnet add package TechieRag --source TechieRag-Private
```

---

## Versioning Best Practices

Use Semantic Versioning (SemVer):

- **1.0.0** → Initial release
- **1.0.1** → Bug fixes
- **1.1.0** → New features (backward compatible)
- **2.0.0** → Breaking changes

Update version in `.csproj`:

```xml
<Version>1.0.1</Version>
```

Or use CI/CD to auto-increment:

```powershell
dotnet pack -p:Version=1.0.$BUILD_NUMBER
```

---

## CI/CD Publishing (GitHub Actions Example)

Create `.github/workflows/publish.yml`:

```yaml
name: Publish NuGet Package

on:
  release:
    types: [published]

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Build
        run: dotnet build src/TechieRag/TechieRag.csproj -c Release

      - name: Pack
        run: dotnet pack src/TechieRag/TechieRag.csproj -c Release -o ./nupkg -p:Version=${{ github.event.release.tag_name }}

      - name: Push to NuGet
        run: dotnet nuget push ./nupkg/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```

---

## Quick Start Commands Summary

```powershell
# Build package
dotnet pack src/TechieRag/TechieRag.csproj -c Release -o ./nupkg

# Publish to NuGet.org
dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --api-key YOUR_KEY --source https://api.nuget.org/v3/index.json

# Publish to GitHub Packages
dotnet nuget push ./nupkg/TechieRag.1.0.0.nupkg --source github

# Publish to local folder
copy ./nupkg/*.nupkg C:\LocalNuGet\
```

---

*Last Updated: 2025-12-29*
