﻿using Fap.Core.Infrastructure.Metadata;
using Ganss.XSS;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using Fap.Core.Extensions;

namespace Fap.AspNetCore.Extensions
{
    public static class FormCollectionExtentions
    {
        /// <summary>
        /// JObject对象转换成FapDynamicObject对象
        /// </summary>
        /// <param name="jobj"></param>
        /// <param name="dynamciObj">FapDynamicObject对象</param>
        /// <param name="excludeKeys">指定字段，排除要赋值的字段</param>
        /// <returns></returns>
        public static dynamic ToDynamicObject(this IFormCollection fcs, IEnumerable<FapColumn> columnList, params string[] excludeKeys)
        {
            FapDynamicObject dynamciObj = new FapDynamicObject(columnList);
            ICollection<string> formKeys = fcs.Keys;
            var sanitizer = new HtmlSanitizer();
            foreach (var frmKey in formKeys)
            {
                if (excludeKeys.Contains(frmKey)) continue;

                FapColumn column = columnList.Where(c => c.ColName == frmKey).FirstOrDefault();
                if (column != null)
                {
                    if (column.IsIntType()) //整型
                    {
                        dynamciObj.SetValue(frmKey, fcs[frmKey][0].ToInt());
                    }
                    else if (column.IsLongType()) //长整型
                    {
                        dynamciObj.SetValue(frmKey, fcs[frmKey][0].ToLong());
                    }
                    else if (column.IsDoubleType()) //浮点型
                    {
                        dynamciObj.SetValue(frmKey, fcs[frmKey][0].ToDecimal());
                    }
                    else if (column.CtrlType == FapColumn.CTRL_TYPE_RICHTEXTBOX)
                    {
                        //富文本防止XSS
                        dynamciObj.SetValue(frmKey, sanitizer.Sanitize(fcs[frmKey].ToString()));
                    }
                    else //字符串
                    {
                        dynamciObj.SetValue(frmKey, fcs[frmKey].ToString());
                    }
                }
                else
                {
                    dynamciObj.SetValue(frmKey, fcs[frmKey].ToString());
                }

            }
            return dynamciObj;
        }



    }
}
