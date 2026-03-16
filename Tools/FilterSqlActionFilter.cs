using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebApi
{
   /// <summary>
   /// 防止sql注入的过滤器,被webapiconfig调用。用来全局配置过滤
   /// </summary>
    public class SqlInjectFilter : ActionFilterAttribute
    {
        /// <inheritdoc />
        /// <summary>
        /// </summary>
        /// <param name="filterContext"></param>
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            base.OnActionExecuting(filterContext);
            var actionParameters = filterContext.ActionDescriptor.Parameters;

            var actionArguments = filterContext.ActionArguments;

            foreach (var p in actionParameters)
            {
                var value = filterContext.ActionArguments[p.Name];

                var pType = p.ParameterType;

                if (value == null)
                {
                    continue;
                }
                //如果不是值类型或接口，不需要过滤
                if (!pType.IsClass) continue;

                if (value is string)
                {
                    //对string类型过滤
                    filterContext.ActionArguments[p.Name] = AntiSqlInject.Instance.GetSafetySql(value.ToString());
                }
                else
                {
                    #region
                    //是一个class，对class的属性中，string类型的属性进行过滤
                    //var properties = pType.GetProperties();
                    //foreach (var pp in properties)
                    //{
                    //    var temp = pp.GetValue(value);
                    //    if (temp == null)
                    //    {
                    //        continue;
                    //    }
                    //    pp.SetValue(value, temp is string ? AntiSqlInject.Instance.GetSafetySql(temp.ToString()) : temp);
                    //}
                    //------------------------------------
                    #endregion
                }
            }

        }
    }
}