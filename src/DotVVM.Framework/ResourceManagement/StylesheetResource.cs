using System;
using System.Collections.Generic;
using System.Linq;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Hosting;

namespace DotVVM.Framework.ResourceManagement
{
    /// <summary>
    /// Reference to a CSS file.
    /// </summary>
    [ResourceConfigurationCollectionName("stylesheets")]
    public class StylesheetResource : LinkResourceBase
    {
        public StylesheetResource(IResourceLocation location)
            : base(ResourceRenderPosition.Head, "text/css", location)
        { }

        public override void RenderLink(IResourceLocation location, IHtmlWriter writer, IDotvvmRequestContext context, string resourceName)
        {
            writer.AddAttribute("href", location.GetUrl(context, resourceName));
            writer.AddAttribute("rel", "stylesheet");
            writer.AddAttribute("type", MimeType);
            base.AddIntegrityAttribute(writer, context);
            writer.RenderSelfClosingTag("link");
        }
    }
}
