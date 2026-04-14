using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteApiServer.Dto;
using PlayniteApiServer.Server;

namespace PlayniteApiServer.Controllers
{
    /// <summary>
    /// Generic controller for the "simple" database collections — the ones whose
    /// items are fundamentally identified by name (Platforms, Genres, Tags, …).
    /// All writes marshal onto the UI dispatcher.
    /// </summary>
    internal sealed class LookupController<T> where T : DatabaseObject
    {
        private readonly IItemCollection<T> collection;
        private readonly Dispatcher ui;

        public LookupController(IItemCollection<T> collection, Dispatcher ui)
        {
            this.collection = collection;
            this.ui = ui;
        }

        public void List(RequestContext r)
        {
            var items = collection.Cast<T>().ToList();
            r.WriteJson(200, items);
        }

        public void Get(RequestContext r)
        {
            var id = HttpExtensions.ParseGuidOrThrow(r.PathVars["id"], "id");
            var item = collection.Get(id);
            if (item == null)
            {
                throw new ApiException(404, "Item not found: " + id);
            }
            r.WriteJson(200, item);
        }

        public void Create(RequestContext r)
        {
            var dto = r.ReadJson<NamedDto>();
            if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
            {
                throw new ApiException(400, "Field 'name' is required.");
            }

            var name = dto.Name.Trim();
            var item = InvokeOnUi(() => collection.Add(name));
            r.Response.AddHeader("Location", r.Request.Url.AbsolutePath.TrimEnd('/') + "/" + item.Id);
            r.WriteJson(201, item);
        }

        public void Patch(RequestContext r)
        {
            var id = HttpExtensions.ParseGuidOrThrow(r.PathVars["id"], "id");
            var existing = collection.Get(id);
            if (existing == null)
            {
                throw new ApiException(404, "Item not found: " + id);
            }

            var patch = r.ReadJObject();
            // For lookup items only "name" is updatable.
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name" };
            foreach (var prop in patch.Properties())
            {
                if (!allowed.Contains(prop.Name))
                {
                    throw new ApiException(400, "Field '" + prop.Name + "' is not patchable on this resource.");
                }
            }

            if (patch.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out var nameTok))
            {
                var newName = nameTok?.ToString();
                if (string.IsNullOrWhiteSpace(newName))
                {
                    throw new ApiException(400, "Field 'name' must be non-empty.");
                }
                existing.Name = newName.Trim();
            }

            InvokeOnUi(() => collection.Update(existing));
            r.WriteJson(200, existing);
        }

        public void Delete(RequestContext r)
        {
            var id = HttpExtensions.ParseGuidOrThrow(r.PathVars["id"], "id");
            if (!collection.ContainsItem(id))
            {
                throw new ApiException(404, "Item not found: " + id);
            }

            InvokeOnUi(() => collection.Remove(id));
            r.Response.StatusCode = 204;
            r.Response.ContentLength64 = 0;
        }

        private TResult InvokeOnUi<TResult>(Func<TResult> fn)
        {
            try
            {
                return (TResult)ui.Invoke(fn);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                throw new ApiException(503, "Playnite is shutting down.");
            }
        }

        private void InvokeOnUi(Action fn)
        {
            try
            {
                ui.Invoke(fn);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                throw new ApiException(503, "Playnite is shutting down.");
            }
        }
    }
}
