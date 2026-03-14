using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace PriceCompareWeb.Infrastructure
{
    /// <summary>
    /// Removes all routes for the specified controllers so they are completely
    /// absent from the routing table (return 404) without removing their source code.
    /// Use this to exclude internal/admin controllers from production deployments.
    /// </summary>
    public class DisableControllerConvention : IControllerModelConvention
    {
        private readonly HashSet<string> _names;

        public DisableControllerConvention(params string[] controllerNames)
            => _names = new HashSet<string>(controllerNames, System.StringComparer.OrdinalIgnoreCase);

        public void Apply(ControllerModel controller)
        {
            if (_names.Contains(controller.ControllerName))
                controller.Selectors.Clear();
        }
    }
}
