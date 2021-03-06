﻿using System;
using System.Threading.Tasks;
using Fap.AspNetCore.Model;
using Fap.AspNetCore.ViewModel;
using Fap.Core.Infrastructure.Query;
using Fap.Core.Infrastructure.Metadata;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using Fap.AspNetCore.Binder;
using Fap.Core.Infrastructure.Model;

namespace Fap.AspNetCore.Serivce
{
    public interface IGridFormService
    {
        Task<ResponseViewModel> PersistenceAsync(FormModel formModel);
        ResponseViewModel Persistence(FormModel frmModel);
        Pageable AnalysisPostData(JqGridPostData jqGridPostData);
        PageDataResultView QueryPageDataResultView(JqGridPostData jqGridPostData);
        string ExportExcelData(JqGridPostData model);
        string ExportExcelTemplate(QuerySet querySet);
        bool ImportExcelData(string tableName);
        bool ImportWordTemplate(string tableName);
        bool ImportExcelReportTemplate(string fid);
        string PrintWordTemplate(GridModel gridModel);
        ChartResult EChart(ChartViewModel chartViewModel, JqGridPostData jqGridPostData);
        void DeleteFormulaCase(string caseUid);
        void SaveFormulaItem(FapFormulaItems formula);
        IList<string> FormulaCalculate(string formulaCaseUid);
    }
}