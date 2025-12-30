# Story 7.1: Docker Container Management Service

## Story Overview
**Story ID:** STORY-7.1
**Title:** Docker Container Management Service
**Epic:** Epic 7 - Qdrant Database Management
**Status:** Done
**Story Points:** 5

## Description
As a developer using TechieRagWeb, I want to programmatically manage Docker containers so that I can easily start/stop Qdrant without leaving the application.

## Acceptance Criteria

### AC1: Docker Detection
- [x] Service detects if Docker daemon is running
- [x] Works on both Windows (named pipe) and Linux (Unix socket)
- [x] Returns clear status when Docker is not available

### AC2: Container Lifecycle
- [x] Can check if container exists by name
- [x] Can create Qdrant container with proper configuration
- [x] Can start/stop existing container
- [x] Can remove container with optional force flag

### AC3: Qdrant Configuration
- [x] Creates container with port mappings 6333 and 6334
- [x] Supports optional volume path for persistent storage
- [x] Sets restart policy to "unless-stopped"
- [x] Pulls qdrant/qdrant:latest image if needed

### AC4: Status Reporting
- [x] Returns container status (Running, Stopped, NotFound, etc.)
- [x] Provides progress callback for image pulls

## Technical Specifications

### File Location
`samples/TechieRagWeb/Services/DockerContainerService.cs`

### Interface
```csharp
public interface IDockerContainerService
{
    Task<bool> IsDockerAvailableAsync();
    Task<bool> ContainerExistsAsync(string containerName);
    Task<ContainerStatus> GetContainerStatusAsync(string containerName);
    Task<string> CreateQdrantContainerAsync(string containerName = "techierag-qdrant", string? volumePath = null);
    Task StartContainerAsync(string containerName);
    Task StopContainerAsync(string containerName);
    Task RemoveContainerAsync(string containerName, bool force = false);
    Task PullQdrantImageAsync(IProgress<string>? progress = null);
}

public enum ContainerStatus
{
    NotFound, Created, Running, Paused, Restarting, Exited, Dead
}
```

### NuGet Package
```xml
<PackageReference Include="Docker.DotNet" Version="3.125.15" />
```

## Definition of Done
- [x] Interface and implementation complete
- [x] Registered in DI container
- [x] Unit testable (can mock DockerClient)
- [x] XML documentation on all public members
- [x] Follows coding standards (no underscores)
- [x] Build passes with no errors
