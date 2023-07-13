using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace TodosApi;

internal class ValidationFilter : IEndpointFilter
{
    private static readonly ConcurrentDictionary<IServiceProvider, ValidationFilter> _instances = new();
    private readonly ILogger<ValidationFilter> _logger;

    private ValidationFilter(ILogger<ValidationFilter> logger)
    {
        _logger = logger;
    }

    public static ValidationFilter Create(IServiceProvider serviceProvider)
    {
        return _instances.GetOrAdd(serviceProvider, sp => new(sp.GetRequiredService<ILogger<ValidationFilter>>()));
    }

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validatableParametersMap = context.HttpContext.GetEndpoint()?.Metadata.OfType<ValidationMetadata>()
            .FirstOrDefault()?.ValidatableParametersMap;

        if (validatableParametersMap is null)
        {
            return next(context);
        }

        Dictionary<string, List<string>>? workingErrors = null;

        foreach (var index in validatableParametersMap)
        {
            var validatable = context.GetArgument<IValidatableObject?>(index);
            if (validatable is not null)
            {
                var requiresServices = validatable is IValidatable v && v.RequiresServiceProvider;
                var validationContext = GetValidationContext(validatable, requiresServices ? context.HttpContext.RequestServices : null);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Validating argument at index {index} of type {type}", index, validatable.GetType().Name);
                }
                var results = validatable.Validate(validationContext);

                if (results.Any())
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Argument at index {index} of type {type} has validation errors", index, validatable.GetType().Name);
                    }
                    workingErrors ??= new();

                    foreach (var result in results)
                    {
                        foreach (var member in result.MemberNames)
                        {
                            List<string>? messages;
                            if (!workingErrors.ContainsKey(member))
                            {
                                messages = new();
                                workingErrors.Add(member, messages);
                            }
                            else
                            {
                                messages = workingErrors[member];
                            }
                            messages.Add(result.ErrorMessage ?? "The value provided was invalid.");
                        }
                    }

                    var errors = MapToFinalErrorsResult(workingErrors);
                    var problem = TypedResults.ValidationProblem(errors);
                    return ValueTask.FromResult((object?)problem);
                }
            }
        }
        return next(context);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = """
                        Instance type is statically represented in generic argument TTarget and is declared as dynamically
                        accessing public properties. This ensures the properties on TTarget are preserved. Note that recursive
                        properties of those properties are still not preserved, but this validation doesn't recurse.
                        """)]
    private static ValidationContext GetValidationContext<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TTarget>
        (TTarget instance, IServiceProvider? serviceProvider)
        where TTarget : notnull
    {
        return new ValidationContext(instance, serviceProvider, null);
    }

    private static Dictionary<string, string[]> MapToFinalErrorsResult(Dictionary<string, List<string>> workingErrors)
    {
        var result = new Dictionary<string, string[]>(workingErrors.Count);

        foreach (var fieldErrors in workingErrors)
        {
            if (!result.ContainsKey(fieldErrors.Key))
            {
                result.Add(fieldErrors.Key, fieldErrors.Value.ToArray());
            }
            else
            {
                var existingFieldErrors = result[fieldErrors.Key];
                result[fieldErrors.Key] = existingFieldErrors.Concat(fieldErrors.Value).ToArray();
            }
        }

        return result;
    }
}

internal class ValidationMetadata(int[] map)
{
#pragma warning disable CA1822 // Mark members as static: BUG https://github.com/dotnet/roslyn-analyzers/issues/6573
    public int[] ValidatableParametersMap => map;
#pragma warning restore CA1822
}

internal static class ValidationExtensions
{
    public static IEndpointConventionBuilder AddValidationFilter<TBuilder>(this TBuilder endpoint)
        where TBuilder : IEndpointConventionBuilder
    {
        endpoint.Add(static builder =>
        {
            if (builder.Metadata.OfType<MethodInfo>().FirstOrDefault() is { } methodInfo)
            {
                var parameters = methodInfo.GetParameters();
                List<int>? validatableParametersMap = null;

                for (var i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    if (parameter.ParameterType.IsAssignableTo(typeof(IValidatableObject)))
                    {
                        validatableParametersMap ??= new();
                        validatableParametersMap.Add(i);
                    }
                }

                if (validatableParametersMap?.Count > 0)
                {
                    builder.Metadata.Add(new ValidationMetadata(validatableParametersMap.ToArray()));

                    builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(HttpValidationProblemDetails), new[] { "application/problem+json" }));
                    builder.FilterFactories.Add((effc, next) =>
                    {
                        var filter = ValidationFilter.Create(effc.ApplicationServices);
                        return (efic) =>
                        {
                            return filter.InvokeAsync(efic, next);
                        };
                    });
                }
            }
        });

        return endpoint;
    }
}

internal interface IValidatable : IValidatableObject
{
    public bool RequiresServiceProvider => false;
}
