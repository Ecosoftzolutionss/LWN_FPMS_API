using DFN_BMS.DB;
using DFN_BMS.Models;
using System.Text.Json;

namespace DFN_BMS.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IServiceScopeFactory _scopeFactory;

        public ExceptionMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
        {
            _next = next;
            _scopeFactory = scopeFactory;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await LogToDatabase(context, ex);
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task LogToDatabase(HttpContext context, Exception ex)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var error = new ErrorLog
                {
                    Message = ex.Message,
                    StackTrace = ex.StackTrace,
                    Source = ex.Source,
                    Path = context.Request.Path,
                    Method = context.Request.Method,
                    CreatedAt = DateTime.Now
                };

                db.ErrorLogs.Add(error);
                await db.SaveChangesAsync();
            }
        }

        private Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 500;
            var result = JsonSerializer.Serialize(new
            {StatusCode = 500,
             Message = "Internal Server Error"
            });
            return context.Response.WriteAsync(result);
        }
    }
}
