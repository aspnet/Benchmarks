namespace RunTemplate;

internal interface ITemplateScenario
{
    public abstract Task BuildAsync();

    public abstract Task RunAsync(string urls, Action notifyReady);
}
