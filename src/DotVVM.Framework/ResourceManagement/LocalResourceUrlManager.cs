﻿using DotVVM.Framework.Configuration;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.Hosting.Middlewares;
using DotVVM.Framework.Routing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DotVVM.Framework.ResourceManagement
{
    public class LocalResourceUrlManager : ILocalResourceUrlManager
    {
        private readonly IResourceHashService hasher;
        private readonly RouteBase resourceRoute;
        private readonly DotvvmResourceRepository resources;
        private readonly ConcurrentDictionary<string, string> alternateDirectories;
        private readonly bool suppressVersionHash;

        public LocalResourceUrlManager(DotvvmConfiguration configuration, IResourceHashService hasher)
        {
            this.resourceRoute = new DotvvmRoute("dotvvmResource/{hash}/{name:regex(.*)}", null, null, null, configuration);
            this.hasher = hasher;
            this.resources = configuration.Resources;
            this.alternateDirectories = configuration.Debug ? new ConcurrentDictionary<string, string>() : null;
            this.suppressVersionHash = configuration.Debug;
        }

        public string GetResourceUrl(ILocalResourceLocation resource, IDotvvmRequestContext context, string name) =>
            resourceRoute.BuildUrl(new Dictionary<string, object>
            {
                ["hash"] = GetVersionHash(resource, context, name),
                ["name"] = EncodeResourceName(name)
            });

        protected virtual string EncodeResourceName(string name)
        {
            return name.Replace(":", "---").Replace(".", "--");
        }

        protected virtual string DecodeResourceName(string name)
        {
            return name.Replace("---", ":").Replace("--", ".");
        }

        protected virtual string GetVersionHash(ILocalResourceLocation location, IDotvvmRequestContext context, string name) =>
            suppressVersionHash ? // don't generate the hash iff !Debug, as it clears breakpoints in debugger when url changes
            EncodeResourceName(name) :
            hasher.GetVersionHash(location, context);

        public ILocalResourceLocation FindResource(string url, IDotvvmRequestContext context, out string mimeType)
        {
            mimeType = null;
            if (DotvvmRoutingMiddleware.FindMatchingRoute(new[] { resourceRoute }, context, out var parameters) == null) return null;
            var name = DecodeResourceName((string)parameters["name"]);
            var hash = (string)parameters["hash"];
            if (resources.FindResource(name) is IResource resource)
            {
                var location = FindLocation(resource, out mimeType);
                if (GetVersionHash(location, context, name) == hash) // check if the resource matches so that nobody can gues the url by chance
                {
                    if (alternateDirectories != null)
                        alternateDirectories.GetOrAdd(hash, _ => (location as IDebugFileLocalLocation)?.GetFilePath(context));
                    return location;
                }
            }

            return TryLoadAlternativeFile(name, hash, context);
        }

        private ILocalResourceLocation TryLoadAlternativeFile(string name, string hash, IDotvvmRequestContext context)
        {
            if (alternateDirectories != null && alternateDirectories.TryGetValue(hash, out string filePath) && filePath != null)
            {
                var directory = Path.GetDirectoryName(Path.Combine(context.Configuration.ApplicationPhysicalPath, filePath));
                if (directory != null)
                {
                    var sourceFile = Path.Combine(directory, name);
                    if (File.Exists(sourceFile)) return new LocalFileResourceLocation(sourceFile);
                }
            }
            return null;
        }

        protected ILocalResourceLocation FindLocation(IResource resource, out string mimeType)
        {
            var linkResource = resource as ILinkResource;
            mimeType = linkResource?.MimeType;
            return linkResource
                   ?.GetLocations()
                   ?.OfType<ILocalResourceLocation>()
                   ?.FirstOrDefault();
        }
    }
}
