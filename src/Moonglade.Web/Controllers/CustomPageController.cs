﻿using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Edi.Practice.RequestResponseModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moonglade.Core;
using Moonglade.Model;
using Moonglade.Model.Settings;
using Moonglade.Web.Models;

namespace Moonglade.Web.Controllers
{
    [Route("page")]
    public class CustomPageController : MoongladeController
    {
        private readonly IMemoryCache _cache;
        private readonly CustomPageService _customPageService;
        private static string[] InvalidPageRouteNames => new[] { "index", "manage" };

        public CustomPageController(
            ILogger<CustomPageController> logger,
            IOptions<AppSettings> settings,
            IMemoryCache cache,
            CustomPageService customPageService) : base(logger, settings)
        {
            _cache = cache;
            _customPageService = customPageService;
        }

        [HttpGet("{slug:regex(^(?!-)([[a-zA-Z0-9-]]+)$)}")]
        public async Task<IActionResult> Index(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return BadRequest();
            }

            var cacheKey = $"page-{slug.ToLower()}";
            var pageResponse = await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                var response = await _customPageService.GetPageAsync(slug);
                return response;
            });

            if (pageResponse.IsSuccess)
            {
                if (pageResponse.Item == null)
                {
                    Logger.LogWarning($"Page not found. {nameof(slug)}: '{slug}'");
                    return NotFound();
                }

                return View(pageResponse.Item);
            }
            return ServerError();
        }

        [Authorize]
        [HttpGet("manage")]
        public async Task<IActionResult> Manage()
        {
            var response = await _customPageService.GetPagesMetaAsync();
            return response.IsSuccess ? View(response.Item) : ServerError();
        }

        [Authorize]
        [HttpGet("manage/create")]
        public IActionResult Create()
        {
            var model = new CustomPageEditViewModel();
            return View("CreateOrEdit", model);
        }

        [Authorize]
        [HttpGet("manage/edit/{id:guid}")]
        public async Task<IActionResult> Edit(Guid id)
        {
            var response = await _customPageService.GetPageAsync(id);
            if (response.IsSuccess)
            {
                if (response.Item == null)
                {
                    return NotFound();
                }

                var model = new CustomPageEditViewModel
                {
                    Id = response.Item.Id,
                    Title = response.Item.Title,
                    Slug = response.Item.Slug,
                    MetaDescription = response.Item.MetaDescription,
                    CssContent = response.Item.CssContent,
                    RawHtmlContent = response.Item.RawHtmlContent,
                    HideSidebar = response.Item.HideSidebar
                };

                return View("CreateOrEdit", model);
            }
            return ServerError();
        }

        [Authorize]
        [HttpPost("manage/createoredit")]
        public async Task<IActionResult> CreateOrEdit(CustomPageEditViewModel model, [FromServices] IMemoryCache cache)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    if (InvalidPageRouteNames.Contains(model.Slug.ToLower()))
                    {
                        ModelState.AddModelError(nameof(model.Slug), "Reserved Slug.");
                        return View("CreateOrEdit", model);
                    }

                    var req = new EditCustomPageRequest(model.Id)
                    {
                        HtmlContent = model.RawHtmlContent,
                        CssContent = model.CssContent,
                        HideSidebar = model.HideSidebar,
                        Slug = model.Slug,
                        MetaDescription = model.MetaDescription,
                        Title = model.Title
                    };

                    var response = model.Id == Guid.Empty ?
                        await _customPageService.CreatePageAsync(req) :
                        await _customPageService.EditPageAsync(req);

                    if (response.IsSuccess)
                    {
                        Logger.LogInformation($"User '{User.Identity.Name}' updated custom page id '{response.Item}'");

                        var cacheKey = $"page-{req.Slug.ToLower()}";
                        cache.Remove(cacheKey);

                        return Json(new { PageId = response.Item });
                    }

                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return Json(new FailedResponse(response.Message));
                }

                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(new FailedResponse("Invalid ModelState"));
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error Create or Edit CustomPage.");
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return Json(new FailedResponse(e.Message));
            }
        }

        [Authorize]
        [HttpPost("manage/delete")]
        public async Task<IActionResult> Delete(Guid pageId, string routeName)
        {
            try
            {
                var response = await _customPageService.DeletePageAsync(pageId);
                if (response.IsSuccess)
                {
                    var cacheKey = $"page-{routeName.ToLower()}";
                    _cache.Remove(cacheKey);

                    return Json(pageId);
                }

                return ServerError();
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error Delete CustomPage, Id: {pageId}.");
                return ServerError();
            }
        }
    }
}