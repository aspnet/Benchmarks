using System.CommandLine.Binding;
using System.CommandLine.Invocation;

namespace RunTemplate;

internal sealed class ScenarioCommandHandler<TScenario> : ICommandHandler
    where TScenario : ITemplateScenario
{
    private const string ReadyStateText = "Template is running";

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var binder = new ModelBinder<TScenario>();
        var instance = binder.CreateInstance(context.BindingContext);

        if (instance is not TScenario scenario)
        {
            throw new InvalidOperationException($"Unable to create instance of scenario '{typeof(TScenario).FullName}'");
        }

        var urls = context.BindingContext.ParseResult.ValueForOption(GlobalOptions.UrlsOption);

        try
        {
            await scenario.BuildAsync();

            if (!string.IsNullOrEmpty(urls))
            {
                await scenario.RunAsync(urls, notifyReady: () =>
                {
                    Console.WriteLine(ReadyStateText);
                });
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
