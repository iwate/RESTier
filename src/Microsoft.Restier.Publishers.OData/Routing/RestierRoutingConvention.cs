﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Routing;
using System.Web.OData.Extensions;
using System.Web.OData.Routing;
using System.Web.OData.Routing.Conventions;
using Microsoft.Restier.Core;

namespace Microsoft.Restier.Publishers.OData.Routing
{
    /// <summary>
    /// The default routing convention implementation.
    /// </summary>
    internal class RestierRoutingConvention : IODataRoutingConvention
    {
        private const string RestierControllerName = "Restier";
        private const string MethodNameOfGet = "Get";
        private const string MethodNameOfPost = "Post";
        private const string MethodNameOfPut = "Put";
        private const string MethodNameOfPatch = "Patch";
        private const string MethodNameOfDelete = "Delete";
        private const string MethodNameOfPostAction = "PostAction";

        private readonly Func<ApiBase> apiFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestierRoutingConvention" /> class.
        /// </summary>
        /// <param name="apiFactory">The API factory method.</param>
        public RestierRoutingConvention(Func<ApiBase> apiFactory)
        {
            this.apiFactory = apiFactory;
        }

        /// <summary>
        /// Selects OData controller based on parsed OData URI
        /// </summary>
        /// <param name="odataPath">Parsed OData URI</param>
        /// <param name="request">Incoming HttpRequest</param>
        /// <returns>Prefix for controller name</returns>
        public string SelectController(ODataPath odataPath, HttpRequestMessage request)
        {
            Ensure.NotNull(odataPath, "odataPath");
            Ensure.NotNull(request, "request");

            if (IsMetadataPath(odataPath))
            {
                return null;
            }

            // If user has defined something like PeopleController for the entity set People,
            // Then whether there is an action in that controller is checked
            // If controller has action for request, will be routed to that controller.
            // Cannot mark EntitySetRoutingConversion has higher priority as there will no way
            // to route to RESTier controller if there is EntitySet controller but no related action.
            if (HasControllerForEntitySetOrSingleton(odataPath, request))
            {
                // Fall back to routing conventions defined by OData Web API.
                return null;
            }

            // Create ApiBase instance
            request.SetApiInstance(apiFactory.Invoke());
            return RestierControllerName;
        }

        /// <summary>
        /// Selects the appropriate action based on the parsed OData URI.
        /// </summary>
        /// <param name="odataPath">Parsed OData URI</param>
        /// <param name="controllerContext">Context for HttpController</param>
        /// <param name="actionMap">Mapping from action names to HttpActions</param>
        /// <returns>String corresponding to controller action name</returns>
        public string SelectAction(
            ODataPath odataPath,
            HttpControllerContext controllerContext,
            ILookup<string, HttpActionDescriptor> actionMap)
        {
            // TODO GitHubIssue#44 : implement action selection for $ref, navigation scenarios, etc.
            Ensure.NotNull(odataPath, "odataPath");
            Ensure.NotNull(controllerContext, "controllerContext");
            Ensure.NotNull(actionMap, "actionMap");

            if (!(controllerContext.Controller is RestierController))
            {
                // RESTier cannot select action on controller which is not RestierController.
                return null;
            }

            HttpMethod method = controllerContext.Request.Method;

            if (method == HttpMethod.Get && !IsMetadataPath(odataPath))
            {
                return MethodNameOfGet;
            }

            ODataPathSegment lastSegment = odataPath.Segments.LastOrDefault();
            if (lastSegment != null
                && (lastSegment.SegmentKind == ODataSegmentKinds.UnboundAction
                || lastSegment.SegmentKind == ODataSegmentKinds.Action))
            {
                return MethodNameOfPostAction;
            }

            if (method == HttpMethod.Post)
            {
                return MethodNameOfPost;
            }

            if (method == HttpMethod.Delete)
            {
                return MethodNameOfDelete;
            }

            if (method == HttpMethod.Put)
            {
                return MethodNameOfPut;
            }

            if (method == new HttpMethod("PATCH"))
            {
                return MethodNameOfPatch;
            }

            return null;
        }

        private static bool IsMetadataPath(ODataPath odataPath)
        {
            return odataPath.PathTemplate == "~" ||
                odataPath.PathTemplate == "~/$metadata";
        }

        private static bool HasControllerForEntitySetOrSingleton(
            ODataPath odataPath, HttpRequestMessage request)
        {
            string controllerName = null;

            ODataPathSegment firstSegment = odataPath.Segments.FirstOrDefault();
            if (firstSegment != null)
            {
                var entitySetSegment = firstSegment as EntitySetPathSegment;
                if (entitySetSegment != null)
                {
                    controllerName = entitySetSegment.EntitySetName;
                }
                else
                {
                    var singletonSegment = firstSegment as SingletonPathSegment;
                    if (singletonSegment != null)
                    {
                        controllerName = singletonSegment.SingletonName;
                    }
                }
            }

            if (controllerName != null)
            {
                var services = request.GetConfiguration().Services;

                var controllers = services.GetHttpControllerSelector().GetControllerMapping();
                HttpControllerDescriptor descriptor;
                if (controllers.TryGetValue(controllerName, out descriptor) && descriptor != null)
                {
                    // If there is a controller, check whether there is an action
                    if (HasSelectableAction(request, descriptor))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasSelectableAction(HttpRequestMessage request, HttpControllerDescriptor descriptor)
        {
            var configuration = request.GetConfiguration();
            var actionSelector = configuration.Services.GetActionSelector();

            // Empty route as this is must and route data is not used by OData routing conversion
            var route = new HttpRoute();
            var routeData = new HttpRouteData(route);

            var context = new HttpControllerContext(configuration, routeData, request)
            {
                ControllerDescriptor = descriptor
            };

            try
            {
                var action = actionSelector.SelectAction(context);
                if (action != null)
                {
                    return true;
                }
            }
            catch (HttpResponseException)
            {
                // ignored
            }

            return false;
        }
    }
}
