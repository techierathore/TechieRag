using TechieRag.Embedded;
using TechieRag.Local;
using TechieRag.Models;

namespace TechieRag.Probe;

/// <summary>
/// The probe's one screen: what will run, the button, the download line, the top result and the
/// timings (REQ-FN-056). Every control carries an <c>AutomationId</c> so native automation (Windows
/// UI Automation, Appium on Android, iOS and Mac Catalyst) drives it element by element.
/// </summary>
public sealed class MainPage : ContentPage
{
    private readonly ProbeRunner runner;
    private readonly Button runButton;
    private readonly Label statusLabel;
    private readonly Label downloadLabel;
    private readonly Label topResultLabel;
    private readonly Label timingsLabel;
    private readonly Label resultLineLabel;
    private readonly LocalProbeRunner localRunner;
    private readonly Button generateButton;
    private readonly Label generateStatusLabel;
    private readonly Label generatedSentenceLabel;
    private readonly Label generationTimingsLabel;
    private bool autoRunDone;

    /// <summary>Builds the page.</summary>
    /// <param name="runner">The action the button runs.</param>
    public MainPage(ProbeRunner runner)
    {
        this.runner = runner;
        Title = "TechieRag Probe";

        var model = EmbeddedModel.PlatformDefault;
        runButton = new Button { Text = "Embed, store, search", AutomationId = "RunEmbedButton" };
        runButton.Clicked += async (_, _) => await RunAsync();

        statusLabel = new Label { Text = "Ready", AutomationId = "StatusLabel", FontAttributes = FontAttributes.Bold };
        downloadLabel = new Label { Text = model.IsDownloaded() ? "Model on disk" : $"First run downloads {model.DownloadSize}", AutomationId = "DownloadLabel" };
        topResultLabel = new Label { Text = "Top result: —", AutomationId = "TopResultLabel" };
        timingsLabel = new Label { Text = "Timings: —", AutomationId = "TimingsLabel" };
        resultLineLabel = new Label { Text = string.Empty, AutomationId = "ResultLineLabel", FontSize = 11 };

        // REQ-FN-059 / BRD-106: the second button, local-model generation, with its own result labels.
        localRunner = new LocalProbeRunner(ConfirmTermsAsync);
        generateButton = new Button { Text = "Generate one sentence", AutomationId = "RunGenerateButton" };
        generateButton.Clicked += async (_, _) => await GenerateAsync();
        generateStatusLabel = new Label { Text = $"Local model: {LocalProbeRunner.Model}", AutomationId = "GenerateStatusLabel" };
        generatedSentenceLabel = new Label { Text = "Sentence: —", AutomationId = "GeneratedSentenceLabel" };
        generationTimingsLabel = new Label { Text = "Generation: —", AutomationId = "GenerationTimingsLabel" };

        var actions = new VerticalStackLayout { Spacing = 8, AutomationId = "ActionsPanel", Children = { runButton, generateButton } };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new Label { Text = "TechieRag Probe", FontSize = 24, FontAttributes = FontAttributes.Bold, AutomationId = "TitleLabel" },
                    new Label { Text = $"Platform: {DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}", AutomationId = "PlatformLabel" },
                    new Label { Text = $"Model: {model}", AutomationId = "ModelLabel" },
                    actions,
                    statusLabel,
                    topResultLabel,
                    timingsLabel,
                    downloadLabel,
                    generateStatusLabel,
                    generatedSentenceLabel,
                    generationTimingsLabel,

                    // The long paths last, so the result and timings stay on a phone's first screen.
                    new Label { Text = $"Model root: {ModelRoot.Current}", AutomationId = "ModelRootLabel", FontSize = 11 },
                    resultLineLabel
                }
            }
        };

        var downloads = ModelDownloadService.Instance;
        downloads.DownloadSizeKnown += (_, e) =>
            MainThread.BeginInvokeOnMainThread(() => downloadLabel.Text = $"Downloading {e.ModelName}: {e.DisplaySize}");
        downloads.ProgressChanged += (_, p) =>
        {
            if (p.Status == ModelDownloadStatus.Downloading)
            {
                var percent = p.OverallBytesProgressPercent;
                MainThread.BeginInvokeOnMainThread(() => downloadLabel.Text = $"Downloading {p.ModelName}: {percent}% of {ModelDownloadService.FormatBytes(p.TotalBytes)}");
            }
        };
    }

    /// <inheritdoc/>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (autoRunDone)
        {
            return;
        }

        autoRunDone = true;
        if (ProbeLaunch.AutoRun)
        {
            await RunAsync();
        }

        if (ProbeLaunch.AutoRunLocal)
        {
            await GenerateAsync();
        }
    }

    private async Task RunAsync()
    {
        runButton.IsEnabled = false;
        statusLabel.Text = "Running";
        try
        {
            var result = await Task.Run(() => runner.RunAsync(FileSystem.AppDataDirectory));
            downloadLabel.Text = "Model on disk: " + result.ModelDirectory;
            topResultLabel.Text = $"Top result: {result.TopResult} (score {result.TopScore:F3})";
            timingsLabel.Text = "Timings: " + result.Timings;
            resultLineLabel.Text = result.ToLine();
            statusLabel.Text = "Done";
            Report(result.ToLine());
        }
        catch (Exception exception)
        {
            statusLabel.Text = "Error: " + exception.Message;
            resultLineLabel.Text = "FAIL " + exception.GetType().Name + ": " + exception.Message;
            Report(resultLineLabel.Text);

            // The full stack goes to the console (logcat, the simulator log) for whoever reads the run.
            Console.WriteLine(exception.ToString());
        }
        finally
        {
            runButton.IsEnabled = true;
        }
    }

    private async Task GenerateAsync()
    {
        generateButton.IsEnabled = false;
        generateStatusLabel.Text = "Generating";
        try
        {
            var result = await Task.Run(() => localRunner.RunAsync());
            generatedSentenceLabel.Text = "Sentence: " + result.Sentence;
            generationTimingsLabel.Text = "Generation: " + result.Timings;
            generateStatusLabel.Text = "Done: " + result.ModelName;
            resultLineLabel.Text = result.ToLine();
            Report(result.ToLine());
        }
        catch (Exception exception)
        {
            generateStatusLabel.Text = "Error: " + exception.Message;
            resultLineLabel.Text = "FAIL generate " + exception.GetType().Name + ": " + exception.Message;
            Report(resultLineLabel.Text);
            Console.WriteLine(exception.ToString());
        }
        finally
        {
            generateButton.IsEnabled = true;
        }
    }

    /// <summary>Shows the local model's licence before its one-time download (REQ-RAG-062).</summary>
    private Task<bool> ConfirmTermsAsync(LocalModelTerms terms, CancellationToken cancellationToken) =>
        MainThread.InvokeOnMainThreadAsync(() => DisplayAlertAsync(
            "Download " + terms.DisplayName,
            $"This downloads {ModelDownloadService.FormatBytes(terms.DownloadBytes)} once and is licensed {terms.LicenceName}: {terms.TermsUrl}. Accept the terms?",
            "Accept",
            "Decline"));

    private static void Report(string line)
    {
        Console.WriteLine(ProbeLaunch.ResultPrefix + " " + line);
        try
        {
            File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "probe-result.txt"), line + Environment.NewLine);
        }
        catch (IOException)
        {
            // The on-screen line is the result; the file is a convenience for unattended runs.
        }
    }
}
