using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SimpleStockFlow.Adapters.Rest.Filters;

/// <summary>
/// The body of a 400, written here instead of inherited from the framework. Three things were
/// wrong with the default and all three shipped to whoever called: decision D-C9 asks for a
/// <c>detail</c> and there was none; the <c>title</c> arrived in English at a person who reads
/// Spanish (article XI); and a type mismatch handed back the request type's full name, the JSON
/// path and the byte position it stopped at -- the shape of the server, to a stranger.
/// </summary>
internal static class ValidationProblemFactory
{
    /// <summary>
    /// MVC invents an entry for the bound parameter itself, so a body that fails to deserialise
    /// reports a field the caller never sent. The portal already carried code to drop it; an error
    /// body the client has to filter is not a contract.
    /// </summary>
    private const string BoundParameterName = "request";

    public static IActionResult Build(ActionContext context)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0 || string.Equals(key, BoundParameterName, StringComparison.Ordinal))
                continue;

            fields[Readable(key)] = entry.Errors.Select(error => Explain(error)).Distinct().ToArray();
        }

        // A 400 with no field left to name means the body never bound at all -- malformed JSON,
        // usually. Saying so beats an empty errors object that reads like nothing went wrong.
        var detail = fields.Count > 0
            ? $"Revisa estos campos: {string.Join(", ", fields.Keys)}."
            : "La petición no se pudo leer: el cuerpo no es un JSON válido.";

        var problem = new ValidationProblemDetails(fields)
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Title = "Datos inválidos",
            Status = StatusCodes.Status400BadRequest,
            Detail = detail,
        };

        // §2.2 declares it and the framework used to add it for free; replacing the body took it
        // away with everything else. It is the only thread between a complaint and a log line, and
        // it gives nothing away -- an opaque identifier of this request and nothing more.
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    /// <summary>
    /// Model-state keys arrive as JSON paths (<c>$.price</c>) or as property names, and the two
    /// name the same field to the caller. The leading <c>$.</c> is an artefact of the parser.
    /// </summary>
    private static string Readable(string key)
    {
        // Two sources, two spellings for the same field: the deserialiser reports the JSON path it
        // read ($.name) and a validation attribute reports the CLR property (Name). §1 of the
        // contract says field names travel camelCase, so both have to arrive spelled the one way
        // the caller sent them -- otherwise a client keying off the field name matches one and
        // misses the other depending on which check happened to fail.
        var field = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;

        return field.Length > 0 && char.IsUpper(field[0])
            ? char.ToLowerInvariant(field[0]) + field[1..]
            : field;
    }

    /// <summary>
    /// Every message here comes from the framework and therefore arrives in English, and the
    /// deserialiser's ones carry the server's internals inside the sentence. Rewriting rather than
    /// filtering is deliberate: a filter has to be right about every message the framework might
    /// ever produce, and being wrong once means leaking once.
    /// </summary>
    private static string Explain(ModelError error) =>
        error.ErrorMessage.Contains("required", StringComparison.OrdinalIgnoreCase)
            ? "Este campo es obligatorio."
            : "El valor no tiene el formato que este campo espera.";
}
