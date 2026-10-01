// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.MasterServer.Features.Registry;
using Microsoft.AspNetCore.Mvc;

namespace KcdMp.MasterServer.Features.Listing.Controllers;

/// <summary>
/// Is the master up, and what does it think it is holding.
///
/// One endpoint rather than the admin blueprint the old service left as a TODO:
/// this is what an uptime check needs, and it also gives the launcher a way to
/// tell "the master is unreachable" from "the master is fine and nobody is
/// hosting", which otherwise look identical from an empty list.
/// </summary>
[ApiController]
// Must stay in step with MasterApi.StatusPath; see ServersController.
[Route("api/v1/status")]
public class StatusController : ControllerBase
{
	private readonly ServerRegistry _registry;

	public StatusController(ServerRegistry registry)
	{
		_registry = registry;
	}

	[HttpGet]
	[ProducesResponseType(StatusCodes.Status200OK)]
	public ActionResult<MasterStatus> Get()
	{
		return Ok(new MasterStatus
		{
			ApiVersion = MasterApi.Version,
			Servers = _registry.Count,
			Players = _registry.TotalPlayers,
		});
	}
}
