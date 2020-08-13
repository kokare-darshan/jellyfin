using System.ComponentModel.DataAnnotations;
using Jellyfin.Api.Constants;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.QuickConnect;
using MediaBrowser.Model.QuickConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// Quick connect controller.
    /// </summary>
    public class QuickConnectController : BaseJellyfinApiController
    {
        private readonly IQuickConnect _quickConnect;
        private readonly IUserManager _userManager;
        private readonly IAuthorizationContext _authContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="QuickConnectController"/> class.
        /// </summary>
        /// <param name="quickConnect">Instance of the <see cref="IQuickConnect"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="authContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
        public QuickConnectController(
            IQuickConnect quickConnect,
            IUserManager userManager,
            IAuthorizationContext authContext)
        {
            _quickConnect = quickConnect;
            _userManager = userManager;
            _authContext = authContext;
        }

        /// <summary>
        /// Gets the current quick connect state.
        /// </summary>
        /// <response code="200">Quick connect state returned.</response>
        /// <returns>The current <see cref="QuickConnectState"/>.</returns>
        [HttpGet("Status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<QuickConnectState> GetStatus()
        {
            _quickConnect.ExpireRequests();
            return Ok(_quickConnect.State);
        }

        /// <summary>
        /// Initiate a new quick connect request.
        /// </summary>
        /// <param name="friendlyName">Device friendly name.</param>
        /// <response code="200">Quick connect request successfully created.</response>
        /// <response code="401">Quick connect is not active on this server.</response>
        /// <returns>A <see cref="QuickConnectResult"/> with a secret and code for future use or an error message.</returns>
        [HttpGet("Initiate")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<QuickConnectResult> Initiate([FromQuery] string? friendlyName)
        {
            return Ok(_quickConnect.TryConnect(friendlyName));
        }

        /// <summary>
        /// Attempts to retrieve authentication information.
        /// </summary>
        /// <param name="secret">Secret previously returned from the Initiate endpoint.</param>
        /// <response code="200">Quick connect result returned.</response>
        /// <response code="404">Unknown quick connect secret.</response>
        /// <returns>An updated <see cref="QuickConnectResult"/>.</returns>
        [HttpGet("Connect")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<QuickConnectResult> Connect([FromQuery] string? secret)
        {
            try
            {
                var result = _quickConnect.CheckRequestStatus(secret);
                return Ok(result);
            }
            catch (ResourceNotFoundException)
            {
                return NotFound("Unknown secret");
            }
        }

        /// <summary>
        /// Temporarily activates quick connect for five minutes.
        /// </summary>
        /// <response code="204">Quick connect has been temporarily activated.</response>
        /// <response code="403">Quick connect is unavailable on this server.</response>
        /// <returns>An <see cref="NoContentResult"/> on success.</returns>
        [HttpPost("Activate")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public ActionResult Activate()
        {
            if (_quickConnect.State == QuickConnectState.Unavailable)
            {
                return Forbid("Quick connect is unavailable");
            }

            _quickConnect.Activate();
            return NoContent();
        }

        /// <summary>
        /// Enables or disables quick connect.
        /// </summary>
        /// <param name="status">New <see cref="QuickConnectState"/>.</param>
        /// <response code="204">Quick connect state set successfully.</response>
        /// <returns>An <see cref="NoContentResult"/> on success.</returns>
        [HttpPost("Available")]
        [Authorize(Policy = Policies.RequiresElevation)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult Available([FromQuery] QuickConnectState? status)
        {
            _quickConnect.SetState(status ?? QuickConnectState.Available);
            return NoContent();
        }

        /// <summary>
        /// Authorizes a pending quick connect request.
        /// </summary>
        /// <param name="code">Quick connect code to authorize.</param>
        /// <response code="200">Quick connect result authorized successfully.</response>
        /// <response code="400">Missing quick connect code.</response>
        /// <returns>Boolean indicating if the authorization was successful.</returns>
        [HttpPost("Authorize")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<bool> Authorize([FromQuery, Required] string? code)
        {
            if (code == null)
            {
                return BadRequest("Missing code");
            }

            return Ok(_quickConnect.AuthorizeRequest(Request, code));
        }

        /// <summary>
        /// Deauthorize all quick connect devices for the current user.
        /// </summary>
        /// <response code="200">All quick connect devices were deleted.</response>
        /// <returns>The number of devices that were deleted.</returns>
        [HttpPost("Deauthorize")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<int> Deauthorize()
        {
            var userId = _authContext.GetAuthorizationInfo(Request).UserId;
            return _quickConnect.DeleteAllDevices(userId);
        }
    }
}
