using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SimpleStockFlow.Bootstrap.Composition;

/// <summary>
/// Hangs the bearer requirement on the operations that actually demand one.
///
/// Declaring it once for the whole document is shorter and wrong: it puts a padlock on
/// POST /api/auth/login and on /health, the two anonymous endpoints of the system, and a reader
/// would conclude they need a token to reach the only place a token comes from.
/// </summary>
internal sealed class BearerWhereRequiredFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        // AllowAnonymous wins over Authorize at run time, so it wins here too, or the page would
        // describe a rule the host does not apply.
        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any())
            return;

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            }] = Array.Empty<string>(),
        });
    }
}

/// <summary>
/// Adds GET /media/{key}, the sixteenth route of the system and the only one no controller
/// serves: UseStaticFiles answers it straight off the binary volume, so it never reaches the
/// API explorer and a generated document would leave it out. Leaving it out would be the
/// misleading option -- the addresses in every product's imageUrl point here, and whoever reads
/// this page has to know the route is anonymous and cannot be made otherwise.
/// </summary>
internal sealed class MediaRouteFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        var operation = new OpenApiOperation
        {
            Tags = { new OpenApiTag { Name = "Media" } },
            Summary = "The bytes of a stored image (E-15).",
            Description =
                "Served by static files, not by a controller, which is exactly why it is anonymous "
                + "and cannot be protected without changing how it is served: the img tag of the "
                + "portal carries no headers. The key cannot be guessed or enumerated, but a leaked "
                + "address serves the image to anyone, for good.",
            Parameters =
            {
                new OpenApiParameter
                {
                    Name = "key",
                    In = ParameterLocation.Path,
                    Required = true,
                    Description = "A 32-character identifier plus the original extension, as published in imageUrl.",
                    Schema = new OpenApiSchema { Type = "string" },
                    Example = new OpenApiString("9f2c8b1e4a7d40f3b6c5e8a1d2f3b4c5.jpg"),
                },
            },
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "The image, with the content type it was stored under.",
                    Content =
                    {
                        ["image/jpeg"] = new OpenApiMediaType(),
                        ["image/png"] = new OpenApiMediaType(),
                        ["image/webp"] = new OpenApiMediaType(),
                    },
                },
                ["404"] = new OpenApiResponse { Description = "No image is stored under that key. No body." },
            },
        };

        document.Paths.Add("/media/{key}", new OpenApiPathItem { Operations = { [OperationType.Get] = operation } });
    }
}

/// <summary>
/// Marks the date range as required in the document, which is what it has always been in the API.
///
/// The controllers take <c>from</c> and <c>to</c> as <c>string?</c> on purpose: a bound
/// <c>DateTimeOffset</c> would let the framework answer its own 400 before the action runs, and
/// the sentence naming which end of the range is missing would never be reachable (D-C4, D-C3).
/// Swashbuckle reads the nullable and concludes "optional", so the document said a call with no
/// range was legal while the API answered 400 to it -- and a client generated from the document
/// compiled a call that fails every time (defect A-10). The binding stays as it is; only the
/// description is corrected, which is the half that was lying.
/// </summary>
internal sealed class MandatoryDateRangeFilter : IOperationFilter
{
    private static readonly string[] RangeEnds = ["from", "to"];

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var parameter in operation.Parameters ?? [])
        {
            if (RangeEnds.Contains(parameter.Name, StringComparer.Ordinal))
                parameter.Required = true;
        }
    }
}

/// <summary>
/// Puts every non-nullable property into the schema's <c>required</c> list.
///
/// <c>SupportNonNullableReferenceTypes</c> stops the generator calling them nullable but does not
/// make them required, and the difference matters to the only audience this document has: a client
/// generated from a schema with no required list treats every field as optional, so
/// <c>totalPages</c> -- the field D-C11 exists to protect -- arrives as "maybe absent" and the
/// paginator that reads it has to guess. Nullable properties are left alone, which is why the
/// problem bodies and <c>imageUrl</c> stay optional: they genuinely are.
/// </summary>
internal sealed class RequiredWhereNotNullableFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null || schema.Properties.Count == 0)
            return;

        foreach (var (name, property) in schema.Properties)
        {
            if (!property.Nullable)
                schema.Required.Add(name);
        }
    }
}
