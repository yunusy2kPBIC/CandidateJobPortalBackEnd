using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CandidatePortal.Api.Controllers;

[AllowAnonymous]
[Route("api/public/route-test")]
public sealed class PublicRouteTestController : PortalControllerBase
{
    [HttpGet]
    [Produces("text/html")]
    public ContentResult Page() => Content("""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>API POST route check</title>
          <style>
            body { font: 16px system-ui, sans-serif; max-width: 640px; margin: 3rem auto; padding: 0 1rem; color: #172033; }
            h1 { font-size: 1.6rem; }
            p { color: #536176; }
            input, button { box-sizing: border-box; font: inherit; padding: .7rem; }
            input { width: 100%; margin: .4rem 0 1rem; }
            button { cursor: pointer; }
            pre { white-space: pre-wrap; overflow-wrap: anywhere; background: #f3f6fa; padding: 1rem; }
          </style>
        </head>
        <body>
          <h1>API POST route check</h1>
          <p>This anonymous diagnostic accepts a short message and discards it. It does not save submissions.</p>
          <form id="test-form">
            <label for="message">Test message</label>
            <input id="message" name="message" maxlength="200" required value="hello from the browser">
            <button type="submit">Send test POST</button>
          </form>
          <pre id="result" aria-live="polite">Ready</pre>
          <script>
            const form = document.getElementById('test-form');
            const result = document.getElementById('result');
            form.addEventListener('submit', async (event) => {
              event.preventDefault();
              result.textContent = 'Sending POST...';
              try {
                const response = await fetch(window.location.pathname, {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ message: document.getElementById('message').value })
                });
                const body = await response.text();
                result.textContent = `HTTP ${response.status} ${response.statusText}\n${body}`;
              } catch (error) {
                result.textContent = `Request failed: ${error}`;
              }
            });
          </script>
        </body>
        </html>
        """, "text/html; charset=utf-8");

    [HttpPost]
    [EnableRateLimiting("public-route-test")]
    public ActionResult<PublicRouteTestResponse> Submit(PublicRouteTestPayload payload)
    {
        var message = payload.Message.Trim();
        return Ok(new PublicRouteTestResponse(true, message.Length, DateTimeOffset.UtcNow));
    }
}

public sealed class PublicRouteTestPayload
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Message { get; init; } = "";
}

public sealed record PublicRouteTestResponse(
    bool Received,
    int MessageLength,
    DateTimeOffset ReceivedAtUtc);
