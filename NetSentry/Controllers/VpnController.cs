using Microsoft.AspNetCore.Mvc;
using NetSentry.DTOs.Requests;
using NetSentry.DTOs.Responses;
using NetSentry.ResultPattern;
using NetSentry.Services;

namespace NetSentry.Controllers
{
    [ApiController]
    [Route("api/vpn")]
    public class VpnController(ITunnelService tunnelService) : ControllerBaseWithResult
    {
        [HttpPost("tunnel")]
        public async Task<IActionResult> CreateTunnel([FromBody] CreateTunnelRequest request)
        {
            var result = await tunnelService.CreateAsync(request.PeerName, request.DurationHours);
            return result.Match(
                onSuccess: () => Ok(TunnelConfigResponse.FromConfig(result.Value)),
                onFailure: err => err.Code switch
                {
                    "Tunnel.InvalidRequest" => BadRequest(new { err.Code, err.Description }),
                    _ => Problem(err)
                }
            );
        }

        [HttpGet("tunnel/{tunnelId}")]
        public async Task<IActionResult> GetTunnel(string tunnelId)
        {
            var result = await tunnelService.GetAsync(tunnelId);
            return result.Match(
                onSuccess: () => Ok(TunnelConfigResponse.FromConfig(result.Value)),
                onFailure: err => err.Code switch
                {
                    "TunnelNotFound" => NotFound(new { err.Code, err.Description }),
                    _ => Problem(err)
                }
            );
        }

        [HttpDelete("tunnel/{tunnelId}")]
        public async Task<IActionResult> DeleteTunnel(string tunnelId)
        {
            var result = await tunnelService.DeleteAsync(tunnelId);
            return result.Match(
                onSuccess: NoContent,
                onFailure: err => err.Code switch
                {
                    "TunnelNotFound" => NotFound(new { err.Code, err.Description }),
                    _ => Problem(err)
                }
            );
        }
    }
}