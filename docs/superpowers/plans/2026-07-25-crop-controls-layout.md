# Crop Controls Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep every crop-exclusion label and input visible in the fixed-width left pane.

**Architecture:** Replace the non-wrapping horizontal crop-control stack with a two-column, two-row XAML grid. Retain the four existing named text boxes so the crop parsing and persistence code remain unchanged. A source-shape test protects the layout structure.

**Tech Stack:** WPF XAML, .NET 8 xUnit tests.

## Global Constraints

- Do not change OCR, crop validation, persistence, or control names.
- Keep the change limited to the left-pane crop controls and their regression test.

---

### Task 1: Make the crop controls fit the left pane

**Files:**
- Create: `tests/TateScribe.Tests/MainWindowLayoutTests.cs`
- Modify: `src/TateScribe.App/MainWindow.xaml:29-41`

**Interfaces:**
- Consumes: existing `CropLeftPercent`, `CropTopPercent`, `CropBottomPercent`, and `CropRightPercent` text boxes.
- Produces: a two-row, two-column crop-control grid that preserves those names.

- [ ] **Step 1: Write the failing test**

Create `MainWindowLayoutTests.cs` with:

```csharp
[Fact]
public void Crop_controls_use_a_two_row_grid_with_all_four_named_inputs()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "src", "TateScribe.App", "MainWindow.xaml"));
    Assert.Contains("<Grid.RowDefinitions>", xaml, StringComparison.Ordinal);
    Assert.Contains("x:Name=\"CropLeftPercent\"", xaml, StringComparison.Ordinal);
    Assert.Contains("x:Name=\"CropTopPercent\"", xaml, StringComparison.Ordinal);
    Assert.Contains("x:Name=\"CropBottomPercent\"", xaml, StringComparison.Ordinal);
    Assert.Contains("x:Name=\"CropRightPercent\"", xaml, StringComparison.Ordinal);
    Assert.Contains("Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\\tests\\TateScribe.Tests\\TateScribe.Tests.csproj --filter FullyQualifiedName~MainWindowLayoutTests`

Expected: FAIL because the crop inputs use one horizontal `StackPanel` and have no grid rows.

- [ ] **Step 3: Write minimal implementation**

Replace the crop input row with:

```xml
<Grid HorizontalAlignment="Center">
  <Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>
  <Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition /></Grid.ColumnDefinitions>
  <!-- existing named text boxes, one label/input pair per cell -->
</Grid>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\\tests\\TateScribe.Tests\\TateScribe.Tests.csproj --filter FullyQualifiedName~MainWindowLayoutTests`

Expected: PASS.

- [ ] **Step 5: Verify the application build**

Run: `dotnet build .\\src\\TateScribe.App\\TateScribe.App.csproj -c Release --no-restore`

Expected: build succeeds with zero errors.
