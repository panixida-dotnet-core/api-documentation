using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PANiXiDA.Core.ApiDocumentation.DependencyInjection;

/// <summary>Registers renderer-independent API documentation services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers one transport source and the common catalog.</summary>
    /// <typeparam name="TSource">The document source implementation.</typeparam>
    /// <param name="services">The application services.</param>
    /// <returns>The application services.</returns>
    public static IServiceCollection AddApiDocumentSource<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>(this IServiceCollection services)
        where TSource : class, IApiDocumentSource
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ApiDocumentCatalog>();
        services.TryAddSingleton<ApiSchemaRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IApiDocumentSource, TSource>());
        return services;
    }

    /// <summary>Registers source-generated schemas for documented messages and results.</summary>
    /// <param name="services">The application services.</param>
    /// <param name="contexts">JSON contexts matching the application's payload serialization.</param>
    /// <returns>The application services.</returns>
    public static IServiceCollection AddApiDocumentationSchemas(this IServiceCollection services,
        params JsonSerializerContext[] contexts)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contexts);
        if (contexts.Any(context => context is null))
        {
            throw new ArgumentException("JSON contexts must not contain null.", nameof(contexts));
        }

        services.TryAddSingleton<ApiSchemaRegistry>();
        foreach (var context in contexts)
        {
            services.AddSingleton(context);
        }

        return services;
    }
}
