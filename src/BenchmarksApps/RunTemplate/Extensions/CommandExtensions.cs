using RunTemplate;

namespace System.CommandLine;

internal static class CommandExtensions
{
    public static Command WithTemplateScenarioHandler<TScenario>(this Command command)
        where TScenario : ITemplateScenario
    {
        command.Handler = new ScenarioCommandHandler<TScenario>();
        return command;
    }
}
