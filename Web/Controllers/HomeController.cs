﻿using System;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.WebPages;
using Compilify.Models;
using Compilify.Web.Commands;
using Compilify.Web.Models;
using Compilify.Web.Queries;
using Raven.Client;

namespace Compilify.Web.Controllers
{
    public class HomeController : BaseMvcController
    {
        private const string ProjectCookieKey = "compilify.currentproject";

        private readonly IDocumentSession session;

        public HomeController(IDocumentSession documentSession)
        {
            session = documentSession;
        }

        protected virtual string CurrentProjectId
        {
            get
            {
                return Request.Cookies[ProjectCookieKey] != null 
                    ? Request.Cookies[ProjectCookieKey].Value 
                    : string.Empty;
            }
            set
            {
                Response.AppendCookie(new HttpCookie(ProjectCookieKey, value)
                                      {
                                          Expires = DateTime.UtcNow.AddDays(7)
                                      });
            }
        }

        [HttpGet]
        public ActionResult Index()
        {
            Project project;

            if (CurrentProjectId.IsEmpty() || (project = session.Load<Project>(CurrentProjectId)) == null)
            {
                project = new Project()
                    .AddOrUpdate(new Document { Name = "Main", Content = "return new Person(\"stranger\").Greet();" })
                    .AddOrUpdate(BuildSampleDocument());

                session.Store(project);
                CurrentProjectId = project.Id;
            }

            return View("Show", new WorkspaceState { Project = project });
        }

        [HttpGet]
        public ActionResult About()
        {
            return View();
        }

        [HttpGet]
        public async Task<ActionResult> Show(string id)
        {
            var project = await Resolve<ProjectByIdQuery>().Execute(id);

            if (project == null)
            {
                return ProjectNotFound();
            }

            return View("Show", new WorkspaceState { Project = project });
        }

        [HttpPost]
        [ValidateInput(false)]
        public async Task<ActionResult> Save(Project project)
        {
            var result = await Resolve<SavePostCommand>().Execute(project);

            if (Request.IsAjaxRequest())
            {
                return Json(new { id = result.Id, location = @Url.RouteUrl("Show", new { id = result.Id }) });
            }

            return RedirectToAction("Show", new { id = result.Id });
        }

        [HttpPost]
        [ValidateInput(false)]
        public ActionResult Validate(Project postViewModel)
        {
            var errors = Resolve<ErrorsInPostQuery>().Execute(postViewModel);
            return Json(new { status = "ok", data = errors });
        }

        private static Document BuildSampleDocument()
        {
            var post = new Document { Name = "Person" };

            var builder = new StringBuilder();
            post.Content = builder.AppendLine("public class Person")
                                  .AppendLine("{")
                                  .AppendLine("    public Person(string name)")
                                  .AppendLine("    {")
                                  .AppendLine("        Name = name;")
                                  .AppendLine("    }")
                                  .AppendLine()
                                  .AppendLine("    public string Name { get; private set; }")
                                  .AppendLine()
                                  .AppendLine("    public string Greet()")
                                  .AppendLine("    {")
                                  .AppendLine("        if (string.IsNullOrEmpty(Name))")
                                  .AppendLine("        {")
                                  .AppendLine("            return \"Hello, stranger!\";")
                                  .AppendLine("        }")
                                  .AppendLine()
                                  .AppendLine("        return string.Format(\"Hello, {0}!\", Name);")
                                  .AppendLine("    }")
                                  .AppendLine("}")
                                  .ToString();

            return post;
        }

        private ActionResult ProjectNotFound()
        {
            Response.StatusCode = 404;

            // TODO: Remove dependency on ViewBag
            ViewBag.Message = string.Format("sorry, we couldn't find that...");

            return View("Error");
        }
    }
}
