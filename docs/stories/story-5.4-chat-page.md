# Story 5.4: Implement Chat Page

## Story Information
**Story ID:** STORY-5.4
**Epic:** EPIC-005 - TechieRagWeb Sample Application
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 5

## Description
Create Chat.razor page with RAG-powered chat interface showing search results with source attribution.

## Acceptance Criteria
- [ ] Chat.razor exists in Components/Pages/
- [ ] Input field for user query
- [ ] Search button or Enter key triggers search
- [ ] Results display with text and relevance scores
- [ ] Source document attribution for each result
- [ ] Top-K selector (5, 10, 20)
- [ ] Optional document filter dropdown
- [ ] Clear results button
- [ ] Loading indicator during search

## Technical Requirements

```razor
@page "/chat"
@inject ITechieRag Rag

<div class="container">
    <h1>RAG Chat</h1>

    <div class="search-box">
        <input @bind="query" @bind:event="oninput"
               @onkeydown="HandleKeyDown" placeholder="Ask a question..." />
        <button @onclick="SearchAsync" disabled="@isSearching">Search</button>
    </div>

    <div class="controls">
        <label>Top K: <select @bind="topK">
            <option value="5">5</option>
            <option value="10">10</option>
            <option value="20">20</option>
        </select></label>
    </div>

    @if (isSearching)
    {
        <div class="loading">Searching...</div>
    }

    @if (results.Any())
    {
        <div class="results">
            @foreach (var result in results)
            {
                <div class="result-card">
                    <div class="score">Score: @result.Score.ToString("P1")</div>
                    <div class="text">@result.Chunk.Text</div>
                    <div class="source">
                        Source: @result.Chunk.Metadata["SourceFile"]
                        @if (result.Chunk.PageNumber.HasValue)
                        {
                            <span>Page @result.Chunk.PageNumber</span>
                        }
                    </div>
                </div>
            }
        </div>
    }
</div>

@code {
    private string query = "";
    private int topK = 5;
    private bool isSearching;
    private List<SearchResult> results = new();

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        isSearching = true;
        StateHasChanged();

        try
        {
            results = (await Rag.SearchAsync(query, topK)).ToList();
        }
        finally
        {
            isSearching = false;
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await SearchAsync();
    }
}
```

## Definition of Done
- [ ] Chat page fully functional
- [ ] Search returns results
- [ ] Results display correctly with sources
- [ ] `dotnet build` passes
