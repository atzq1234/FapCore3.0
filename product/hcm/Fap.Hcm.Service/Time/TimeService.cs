﻿using Dapper;
using Fap.AspNetCore.ViewModel;
using Fap.Core.DataAccess;
using Fap.Core.DI;
using Fap.Core.Infrastructure.Domain;
using Fap.Core.Rbac.Model;
/* ==============================================================================
* 功能描述：  
* 创 建 者：wyf
* 创建日期：2016/5/17 17:20:33
* ==============================================================================*/
using System;
using System.Collections.Generic;
using System.Linq;
using Fap.Core.Extensions;
using Fap.Core.Utility;
using Fap.Core.Infrastructure.Metadata;
using Ardalis.GuardClauses;
using Fap.Core.Exceptions;
using Fap.Core.Extensions;

namespace Fap.Hcm.Service.Time
{
    /// <summary>
    /// 时间管理服务类
    /// </summary>
    [Service]
    public class TimeService : ITimeService
    {
        private readonly IDbContext _dbContext;
        private readonly IFapApplicationContext _applicationContext;
        public TimeService(IDbContext dataAccessor, IFapApplicationContext applicationContext)
        {
            _dbContext = dataAccessor;
            _applicationContext = applicationContext;
        }
        /// <summary>
        /// 添加假日
        /// </summary>
        /// <param name="caseUid">假日套</param>
        /// <param name="list">假日集合</param>
        [Transactional]
        public void AddHoliday(string caseUid, IEnumerable<TmHoliday> list)
        {
            DynamicParameters param = new DynamicParameters();
            param.Add("CaseUid", caseUid);
            _dbContext.DeleteExec("TmHoliday", "CaseUid=@CaseUid", param);
            if (list.Any())
            {
                _dbContext.InsertBatch<TmHoliday>(list);
            }
        }

        /// <summary>
        /// 初始化排班计划
        /// </summary>
        /// <param name="empWhere">员工条件</param>
        /// <param name="shiftUid">班次</param>
        /// <param name="holidayUid">休息日套</param>
        /// <param name="shiftYear">年份</param>
        [Transactional]
        public void Schedule(IEnumerable<Fap.Core.Rbac.Model.Employee> empList, string shiftUid, string holidayUid, string startDate, string endDate)
        {
            Guard.Against.Null(empList, nameof(empList));
            string sheduleUid = UUIDUtils.Fid;
            ScheduleEmployee();
            ScheduleShift();
            void ScheduleShift()
            {
                //获取休息日
                DynamicParameters paramHolidy = new DynamicParameters();
                paramHolidy.Add("CaseUid", holidayUid);
                var listHolidys = _dbContext.QueryWhere<TmHoliday>("CaseUid=@CaseUid", paramHolidy);
                //获取班次
                var Shift = _dbContext.Get<TmShift>(shiftUid);
                if (Shift != null)
                {
                    startDate += " 00:00:00";
                    endDate += " 23:59:59";
                    string currDate = DateTimeUtils.CurrentDateTimeStr;
                    var TimeS = DateTimeUtils.ToDateTime(endDate) - DateTimeUtils.ToDateTime(startDate);
                    var Days = Convert.ToInt32(TimeS.TotalDays);
                    DateTime wDate = DateTimeUtils.ToDateTime(startDate);

                    IList<TmSchedule> listSchedule = new List<TmSchedule>();
                    for (int i = 0; i < Days; i++)
                    {
                        TmSchedule tmSchedule = new TmSchedule();
                        string wd = DateTimeUtils.DateFormat(wDate.AddDays(i));
                        if (listHolidys.Any(w => w.Holiday == wd))
                        {
                            continue;
                        }
                        tmSchedule.ScheduleUid = sheduleUid;
                        tmSchedule.ShiftUid = shiftUid;
                        tmSchedule.WorkDay = wd;
                        //上班时间
                        tmSchedule.StartTime = wd + " " + Shift.StartTime;
                        //下班时间，可能存在跨天=上班时间+工作时长+休息时长
                        tmSchedule.EndTime = DateTimeUtils.DateTimeFormat(DateTimeUtils.ToDateTime(tmSchedule.StartTime).AddHours(Shift.WorkHoursLength).AddMinutes(Shift.RestMinutesLength));
                        //迟到时间
                        tmSchedule.LateTime = DateTimeUtils.DateTimeFormat(DateTimeUtils.ToDateTime(tmSchedule.StartTime).AddMinutes(Convert.ToDouble(Shift.ComeLate)));

                        //排班休息开始时间
                        tmSchedule.RestStartTime = wd + " " + Shift.RestStartTime;
                        //休息时间跨天处理
                        if (DateTimeUtils.ToDateTime(tmSchedule.StartTime) > DateTimeUtils.ToDateTime(tmSchedule.RestStartTime)) //如果排班跨天
                        {
                            //排班休息开始时间
                            tmSchedule.RestStartTime = DateTimeUtils.DateFormat(DateTimeUtils.ToDateTime(wd).AddDays(1)) + " " + Shift.RestStartTime;
                        }
                        //休息结束时间
                        tmSchedule.RestEndTime = DateTimeUtils.DateTimeFormat(DateTimeUtils.ToDateTime(tmSchedule.RestStartTime).AddMinutes(Convert.ToDouble(Shift.RestMinutesLength)));
                        //早退时间
                        tmSchedule.LeaveTime = DateTimeUtils.DateTimeFormat(DateTimeUtils.ToDateTime(tmSchedule.EndTime).AddMinutes(Convert.ToDouble(-Shift.LeftEarly)));
                        //早打卡开始时间
                        tmSchedule.StartCardTime = DateTimeUtils.DateTimeFormat(DateTimeUtils.ToDateTime(tmSchedule.StartTime).AddHours(Convert.ToDouble(-Shift.EarlyCard)));
                        //最晚打卡时间
                        tmSchedule.EndCardTime = DateTimeUtils.DateTimeFormat(DateTimeUtils.ToDateTime(tmSchedule.EndTime).AddHours(Convert.ToDouble(Shift.LateCard)));

                        tmSchedule.WorkHoursLength = Shift.WorkHoursLength;
                        tmSchedule.RestMinutesLength = Shift.RestMinutesLength;

                        listSchedule.Add(tmSchedule);
                    }
                    _dbContext.InsertBatch(listSchedule);

                }
            }
            void ScheduleEmployee()
            {
                //删除存在此区间排班的员工
                string sql = "delete from TmScheduleEmployee where EmpUid in @EmpList and StartDate<@EndDate and EndDate>@StartDate";
                _dbContext.Execute(sql, new DynamicParameters(new { EmpList = empList.Select(e => e.Fid), StartDate = startDate, EndDate = endDate }));
                //add tmshceduleemployee

                IList<TmScheduleEmployee> scheduleEmployeeList = new List<TmScheduleEmployee>();
                foreach (var emp in empList)
                {
                    TmScheduleEmployee schemp = new TmScheduleEmployee();
                    schemp.EmpUid = emp.Fid;
                    schemp.DeptUid = emp.DeptUid;
                    schemp.StartDate = startDate;
                    schemp.EndDate = endDate;
                    schemp.ScheduleUid = sheduleUid;
                    scheduleEmployeeList.Add(schemp);
                }
                _dbContext.InsertBatch(scheduleEmployeeList);
            }
        }

        public void DayResultCalculate(string startDate, string endDate)
        {
            //初始化当月日结果数据通过排班
            InitDayResultBySchedule();
            //更新打卡数据到日结果
            UpdateCardRecordToDayResult();
            //此时间段日结果
            var dayResultList = _dbContext.QueryWhere<TmDayResult>($"{nameof(TmDayResult.CurrDate)}>=@StartDate and {nameof(TmDayResult.CurrDate)}<=@EndDate",
                new DynamicParameters(new { StartDate = startDate, EndDate = endDate }));
            foreach (var dayResult in dayResultList)
            {
                //无请假出差
                if (dayResult.TravelType.IsMissing() && dayResult.LeavelType.IsMissing())
                {
                    //计算打卡
                    CalculateCardRecord(dayResult);
                }
                else
                {
                    //计算出差和请假
                    CalculateTravelAndLeavel(dayResult);

                }
            }
            void CalculateTravelAndLeavel(TmDayResult dayResult)
            {

            }
            void CalculateCardRecord(TmDayResult dayResult)
            {
                //存在实际工作时长
                if (dayResult.StWorkHourLength > 0)
                {
                    if (DateTimeUtils.ToDateTime(dayResult.CardStartTime) > DateTimeUtils.ToDateTime(dayResult.LateTime)
                        &&DateTimeUtils.ToDateTime(dayResult.CardEndTime)>DateTimeUtils.ToDateTime(dayResult.EndTime))
                    {
                        //早打卡迟到且晚打卡大于下班时间
                        dayResult.CalResult = DayResultEnum.ComeLate.Description();
                    }
                    if (DateTimeUtils.ToDateTime(dayResult.CardEndTime) < DateTimeUtils.ToDateTime(dayResult.LeaveTime)&&
                        DateTimeUtils.ToDateTime(dayResult.CardStartTime)<DateTimeUtils.ToDateTime(dayResult.StartTime))
                    {
                        //早打卡正常，晚打卡早于早退时间
                        dayResult.CalResult = DayResultEnum.LeaveEarly.Description();
                    }
                    if(DateTimeUtils.ToDateTime(dayResult.CardStartTime)>DateTimeUtils.ToDateTime(dayResult.LateTime)
                        && DateTimeUtils.ToDateTime(dayResult.CardEndTime) < DateTimeUtils.ToDateTime(dayResult.LeaveTime))
                    {
                        //迟到早退
                        dayResult.CalResult = DayResultEnum.ComeLate.Description() + DayResultEnum.LeaveEarly.Description();
                    }
                    //实际工作时长>工作时长,且无迟到早退
                    if (dayResult.StWorkHourLength >= dayResult.WorkHoursLength)
                    {
                        if (dayResult.CalResult.IsMissing())
                        {
                            dayResult.CalResult = DayResultEnum.Normal.Description();
                        }
                        else
                        {
                            dayResult.CalResult += DayResultEnum.Normal.Description();
                        }
                    }
                    else if(dayResult.StWorkHourLength< dayResult.WorkHoursLength)
                    {

                    }
                }
                else
                {
                    dayResult.CalResult = DayResultEnum.Absence.Description();
                }

            }

            void UpdateCardRecordToDayResult()
            {
                //此时间段(+-1天)所有打卡记录，存在跨天情况
                var cardRecords = _dbContext.QueryWhere<TmCardRecord>($"{nameof(TmCardRecord.CardTime)}>=@StartDate and {nameof(TmCardRecord.CardTime)}<=@EndDate"
                    , new DynamicParameters(new { StartDate = DateTimeUtils.DateTimeFormat(DateTimeUtils.ToDateTime(startDate).AddDays(-1)), EndDate = DateTimeUtils.DateTimeFormat(DateTimeUtils.ToDateTime(endDate).AddDays(1)) }));
                if (!cardRecords.Any())
                {
                    return;
                }
                //此时间段日结果
                var dayResults = _dbContext.QueryWhere<TmDayResult>($"{nameof(TmDayResult.CurrDate)}>=@StartDate and {nameof(TmDayResult.CurrDate)}<=@EndDate",
                    new DynamicParameters(new { StartDate = startDate, EndDate = endDate }));
                foreach (var dayResult in dayResults)
                {
                    //当前日结果中考勤打卡集合
                    var cardTimes = cardRecords.Where(c => c.EmpUid == dayResult.EmpUid && DateTimeUtils.ToDateTime(c.CardTime) >= DateTimeUtils.ToDateTime(dayResult.StartCardTime) && DateTimeUtils.ToDateTime(c.CardTime) <= DateTimeUtils.ToDateTime(dayResult.EndCardTime));
                    if (cardTimes.Any())
                    {
                        dayResult.CardStartTime = cardTimes.Min(c => c.CardTime);
                        dayResult.CardEndTime = cardTimes.Max(c => c.CardTime);
                        dayResult.StWorkHourLength =Math.Round(DateTimeUtils.ToDateTime(dayResult.CardStartTime).Subtract(DateTimeUtils.ToDateTime(dayResult.CardEndTime)).TotalHours-dayResult.RestMinutesLength/60.0,2);
                        _dbContext.Update(dayResult);
                    }
                }
            }

            void InitDayResultBySchedule()
            {
                //获取当前月考勤期间的排班
                var currPeriod = _dbContext.QueryFirstOrDefaultWhere<TmPeriod>($"{nameof(TmPeriod.CurrMonth)}='{DateTimeUtils.CurrentYearMonth}'");
                if (currPeriod == null)
                {
                    Guard.Against.FapBusiness("无当月考勤期间，请设置考勤期间");
                }
                int isExist = _dbContext.Count(nameof(TmDayResult), $"{nameof(TmDayResult.CurrDate)}>='{currPeriod.StartDate}' and {nameof(TmDayResult.CurrDate)}<='{currPeriod.EndDate}'");
                if (isExist > 0)
                {
                    return;
                }
                var schedules = _dbContext.QueryWhere<TmSchedule>($"{nameof(TmSchedule.WorkDay)}>= '{currPeriod.StartDate}' and {nameof(TmSchedule.WorkDay)}<='{currPeriod.EndDate}'");
                if (!schedules.Any())
                {
                    Guard.Against.FapBusiness("未找到当月排班，请设置排班");
                }
                //排班员工
                var scheduleEmployees = _dbContext.QueryWhere<TmScheduleEmployee>($"{nameof(TmScheduleEmployee.ScheduleUid)} in @ScheduleUids", new DynamicParameters(new { ScheduleUids = schedules.Select(s => s.ScheduleUid) }));
                if (!scheduleEmployees.Any())
                {
                    Guard.Against.FapBusiness("未找到次考勤期间的排班员工");
                }
                _dbContext.InsertBatch(InitDayResult());
                IEnumerable<TmDayResult> InitDayResult()
                {
                    foreach (var schedule in schedules)
                    {
                        foreach (var scheduleEmployee in scheduleEmployees)
                        {
                            TmDayResult dayResult = new TmDayResult();
                            dayResult.EmpUid = scheduleEmployee.EmpUid;
                            dayResult.DeptUid = scheduleEmployee.DeptUid;
                            dayResult.ShiftUid = schedule.ShiftUid;
                            dayResult.WorkHoursLength = schedule.WorkHoursLength;
                            dayResult.RestMinutesLength = schedule.RestMinutesLength;
                            dayResult.CurrDate = schedule.WorkDay;
                            dayResult.StartTime = schedule.StartTime;
                            dayResult.EndTime = schedule.EndTime;
                            dayResult.LateTime = schedule.LateTime;
                            dayResult.LeaveTime = schedule.LeaveTime;
                            dayResult.StartCardTime = schedule.StartCardTime;
                            dayResult.EndCardTime = schedule.EndCardTime;
                            dayResult.RestStartTime = schedule.RestStartTime;
                            dayResult.RestEndTime = schedule.RestEndTime;
                            yield return dayResult;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 根据时间段获取员工在当前时间段里的工作天数
        /// </summary>
        /// <returns></returns>
        public double GetEmpWorkDaysByTime(string EmpUid, string StartDate, string EndDate, string LeaveType, out string msg, out double workhours, string Billcode = null)
        {
            msg = "";
            workhours = 0;
            var vStartDate = Convert.ToDateTime(StartDate);
            var vEndDate = Convert.ToDateTime(EndDate);

            DynamicParameters paramWork = new DynamicParameters();
            paramWork.Add("EmpUid", EmpUid);
            paramWork.Add("vStartDate", DateTimeUtils.DateTimeFormat(vStartDate));
            paramWork.Add("vEndDate", DateTimeUtils.DateTimeFormat(vEndDate));
            //获取该时间段是否有休假
            var tm = _dbContext.Count("TmLeaveApply", "AppEmpUid=@EmpUid and ((StartTime<=@vStartDate and EndTime>@vStartDate) or (StartTime<@vEndDate and EndTime>=@vEndDate) or (StartTime>=@vStartDate and EndTime<=@vEndDate)) and (BillStatus='PROCESSING' or BillStatus='PASSED' or BillStatus='SUSPENDED')", paramWork);
            if (tm > 0)
            {
                msg = "当前时间段内已存在请假申请！";
                return 0;
            }
            //获取当前时间段是否有出差
            var cc = _dbContext.Count("TmTravelApply", "AppEmpUid=@EmpUid and ((StartTime<=@vStartDate and EndTime>@vStartDate) or (StartTime<@vEndDate and EndTime>=@vEndDate) or (StartTime>=@vStartDate and EndTime<=@vEndDate)) and (BillStatus='PROCESSING' or BillStatus='PASSED' or BillStatus='SUSPENDED')", paramWork);
            if (cc > 0)
            {
                msg = "当前时间段内已存在出差申请！";
                return 0;
            }
            paramWork.Add("StartDate", vStartDate.ToString("yyyy-MM-dd"));
            paramWork.Add("EndDate", vEndDate.ToString("yyyy-MM-dd"));
            //获取当前时间段内排班天数
            double workDay = (double)_dbContext.Count("TmSchedule", "EmpUid=@EmpUid and WorkDay>=@StartDate and WorkDay<=@EndDate", paramWork);
            if (workDay <= 0)
            {
                msg = "您当前时段内未找到排班信息！";
                return 0;
            }
            workhours = (double)_dbContext.Sum("TmSchedule", "workhourslength", "EmpUid=@EmpUid and WorkDay>=@StartDate and WorkDay<=@EndDate", paramWork);
            //获取请假开始当天的排班
            var startSchedule = _dbContext.QueryFirstOrDefaultWhere<TmSchedule>("WorkDay=@StartDate", paramWork);
            if (startSchedule != null)
            {
                //获取开始请假当天的请假时长

                var startHours = (Convert.ToDateTime(startSchedule.EndTime) - vStartDate).TotalHours;

                //当天请假时长大于1小于排班数一半 按0.5天计算  超过请假时长一半按1天计算 小于1小时的不算当天请假
                if (1 <= startHours && startHours <= (startSchedule.WorkHoursLength / 2))
                {
                    workDay -= 0.5D;
                }
                else if (startHours < 1)
                {
                    workDay -= 1;
                }
                workhours = workhours - startSchedule.WorkHoursLength + startHours;
            }
            //获取请假结束当天的排班
            var endSchedule = _dbContext.QueryFirstOrDefaultWhere<TmSchedule>("WorkDay=@EndDate", paramWork);
            if (endSchedule != null)
            {
                //获取结束请假当天的请假时长
                var endHours = (vEndDate - Convert.ToDateTime(endSchedule.StartTime)).TotalHours;
                if (1 <= endHours && endHours <= (endSchedule.WorkHoursLength / 2))
                {
                    workDay -= 0.5;
                }
                else if (endHours < 1)
                {
                    workDay -= 1;
                }
                workhours = workhours - endSchedule.WorkHoursLength + endHours;
            }
            return workDay;
        }

        /// <summary>
        /// 判断某人在某个时间段内是否有排班
        /// </summary>
        /// <returns></returns>
        public bool ExistEmpWorkDaysByTime(string EmpUid, string StartDate, string EndDate)
        {
            var vStartDate = Convert.ToDateTime(StartDate);
            var vEndDate = Convert.ToDateTime(EndDate);

            DynamicParameters paramWork = new DynamicParameters();
            paramWork.Add("EmpUid", EmpUid);
            paramWork.Add("vStartDate", vStartDate.ToString("yyyy-MM-dd HH:mm"));
            paramWork.Add("vEndDate", vEndDate.ToString("yyyy-MM-dd HH:mm"));
            var tm = _dbContext.Count("TmSchedule", "EmpUid=@EmpUid and ((StartTime<=@vStartDate and EndTime>@vStartDate) or (StartTime<@vEndDate and EndTime>=@vEndDate) or (StartTime>=@vStartDate and EndTime<=@vEndDate))", paramWork);
            if (tm > 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 根据获取人员当前年假天数
        /// </summary>
        /// <param name="EmpUid"></param>
        /// <returns></returns>
        public double GetAnnaulRemainderNum(string EmpUid)
        {

            //string sql = string.Format("select RemainderNum from TmAnnualLeave where EmpUid=@EmpUid and Annual=@Annual");
            DynamicParameters param = new DynamicParameters();
            //根据当前列获取值
            param.Add("EmpUid", EmpUid);
            param.Add("Annual", DateTime.Now.Year);
            double c = _dbContext.ExecuteScalar<double>("select RemainderNum from  TmAnnualLeave where EmpUid=@EmpUid and Annual=@Annual", param);

            return c;

        }



        /// <summary>
        /// 根据假期类型编码获取假期类型
        /// </summary>
        /// <returns></returns>
        public TmLeaveType GetLeaveType(string TypeCode)
        {

            DynamicParameters param = new DynamicParameters();
            param.Add("TypeCode", TypeCode);
            return _dbContext.QueryAll<TmLeaveType>().FirstOrDefault(t => t.TypeCode == TypeCode);
        }
        /// <summary>
        /// 获取人员可调休时长
        /// </summary>
        /// <returns></returns>
        public double GetTuneoffHoursLength(string EmpUid)
        {

            DynamicParameters param = new DynamicParameters();
            param.Add("EmpUid", EmpUid);
            //缺少计算条件
            var hours = _dbContext.ExecuteScalar<double>("select sum(ReHoursLength) from  TmOvertimeStat where EmpUid=@EmpUid and isFalse=0", param);

            return hours;
        }
        /// <summary>
        /// 假期初始化
        /// </summary>
        /// <param name="periodUid">假期区间</param>
        /// <param name="Annual">年度</param>
        [Transactional]
        public void AnnualLeaveInit(string year, string startDate, string endDate)
        {
            DynamicParameters param = new DynamicParameters();
            param.Add("Year", year);
            param.Add("StartDate", startDate);
            param.Add("EndDate", endDate);
            int isExist = _dbContext.Count(nameof(TmAnnualLeave), $"{nameof(TmAnnualLeave.Annual)}=@Year", param);
            if (isExist > 0)
            {
                Guard.Against.FapBusiness("已存在该年度年假，不能再生成！");
            }
            //查找年假规则
            var rules = _dbContext.QueryAll<TmAnnualLeaveRule>();
            if (!rules.Any())
            {
                Guard.Against.FapBusiness("没有找到年假生成规则，请在菜单[基础设置-年假规则]中设置年假规则");
            }
            var cols = _dbContext.Columns("Employee");
            IList<TmAnnualLeave> annualLeaveList = new List<TmAnnualLeave>();
            foreach (var rule in rules)
            {
                if (rule.EmpConditionDesc.IsMissing())
                {
                    continue;
                }
                string sql = $"select {rule.Days} Days,Fid,DeptUid from Employee where {SqlUtils.ParsingSql(cols, rule.EmpConditionDesc, _dbContext.DatabaseDialect)}";
                var emps = _dbContext.Query(sql);
                foreach (var emp in emps)
                {
                    TmAnnualLeave annualLeave = new TmAnnualLeave()
                    {
                        Annual = year,
                        EmpUid = emp.Fid,
                        DeptUid = emp.DeptUid,
                        StartDate = startDate,
                        EndDate = endDate,
                        CurrYearNum = emp.Days,
                        LastYearLeft = 0,
                        CurrRealNum = emp.Days,
                        UsedNum = 0,
                        RemainderNum = emp.Days,
                        IsHandle = 0
                    };
                    annualLeaveList.Add(annualLeave);
                }
            }
            _dbContext.InsertBatch(annualLeaveList);

        }
        /// <summary>
        /// 上年结余假期
        /// </summary>
        /// <param name="year"></param>
        /// <param name="lastYear">上一年</param>
        [Transactional]
        public void AnnualLeaveSurplus(string year, string lastYear)
        {
            DynamicParameters parameters = new DynamicParameters(new { CurrAnnual = year, PreAnnual = lastYear });
            //求结余天数
            string sql = "update a set a.LastYearLeft=b.RemainderNum, a.IsHandle=1  from TmAnnualLeave as a,TmAnnualLeave as b where a.EmpUid=b.EmpUid and a.Annual=@CurrAnnual and a.IsHandle=0 and b.Annual=@PreAnnual";
            //此规则的sql 无法解析。所以用原始执行sql，需要的话手动添加有效条件
            _dbContext.ExecuteOriginal(sql, parameters);

            //求本年实际和剩余天数
            sql = "update TmAnnualLeave set RemainderNum=CurrYearNum+LastYearLeft,CurrRealNum=CurrYearNum+LastYearLeft,IsHandle=1 where Annual=@CurrAnnual";
            _dbContext.Execute(sql, parameters);
        }
        /// <summary>
        /// 生成日结果补签数据
        /// </summary>
        /// <param name="fid">考勤周期Fid</param>
        /// <param name="employee">当前操作人</param>
        [Transactional]
        public void BuilderDayResultBq(string fid)
        {

            DynamicParameters param = new DynamicParameters();
            param.Add("Fid", fid);
            var tmmonth = _dbContext.QueryFirstOrDefault("select Code,StartDate,EndDate from  TmPeriod where Fid=@Fid", param);

            _dbContext.Execute(@"merge into TmReplenDay A
                                   using  (select * from  TmDayResult where dr=0 and CurrDate>=@StartDate and CurrDate<=@EndDate and (CalResult like '%旷工%' or CalResult like '%缺勤%' or CalResult like '%迟%' or CalResult like '%退%')) B on (A.EmpUid=B.EmpUid and A.CurrDate=B.CurrDate)
                                   when not matched then
                                   insert (Fid,EmpUid,TmMonthCode,StartTime,EndTime,CurrDate,ReCalResult,EnableDate,DisableDate,Ts,Dr,CreateBy,CreateName,CreateDate) values(dbo.GetFid(),B.EmpUid,@Code,B.CardStartTime,B.CardEndTime,B.CurrDate,B.CalResult,CONVERT(varchar(100), GETDATE(), 20),'9999-12-31 23:59:59',dbo.GetTs(),0,@UserFid,@UserName,CONVERT(varchar(100), GETDATE()));
                                ", new DynamicParameters(new { StartDate = tmmonth.StartDate, EndDate = tmmonth.EndDate, Code = tmmonth.Code, UserName = _applicationContext.EmpName, UserFid = _applicationContext.EmpUid }));
            //更新员工编码、邮箱、直属上级字段
            _dbContext.Execute($"update TmReplenDay  set EmpCode=Employee.EmpCode,MailBox=Employee.MailBox, LeaderShip=Employee.LeaderShip from Employee where TmReplenDay.EmpUid=Employee.Fid and TmReplenDay.TmMonthCode=@Code ", new DynamicParameters(new { Code = tmmonth.Code }));

            //发消息
            var UserName = _applicationContext.EmpName;
            var UserFid = _applicationContext.EmpUid;
            string baseUrl = _applicationContext.BaseUrl;

            string sql = string.Empty;
            if (_dbContext.DatabaseDialect == DatabaseDialectEnum.MSSQL)
            {
                sql = @"insert into FapMail (Fid,Recipient,RecipientEmailAddress,Subject,MailContent,EnableDate,DisableDate,Ts,Dr,CreateBy,CreateName,CreateDate) 
                            select dbo.GetFid(),(select top 1 EmpName from Employee where Fid=TmReplenDay.EmpUid and dr=0),
                            MailBox,
                            '考勤补签',
                            '您本月有'+cast(count(0) as varchar)+'条考勤异常需处理，详情进入系统查看。',
                             CONVERT(varchar(100), GETDATE(), 20),'9999-12-31 23:59:59',dbo.GetTs(),0,@UserFid,@UserName,CONVERT(varchar(100), GETDATE(),20) 
                            from  TmReplenDay where dr=0 and TmMonthCode=@Code group by EmpUid,MailBox;
                            insert into FapMail (Fid,Recipient,RecipientEmailAddress,Subject,MailContent,EnableDate,DisableDate,Ts,Dr,CreateBy,CreateName,CreateDate) 
                            select dbo.GetFid(),(select top 1 EmpName from Employee where Fid=TmReplenDay.LeaderShip and dr=0),
                            (select top 1 Mailbox from Employee where Fid=TmReplenDay.LeaderShip and dr=0),
                            '考勤补签',
                            '您的直属下级本月有'+cast(count(0) as varchar)+'条考勤异常需处理，详情进入系统查看。',
                             CONVERT(varchar(100), GETDATE(), 20),'9999-12-31 23:59:59',dbo.GetTs(),0,@UserFid,@UserName,CONVERT(varchar(100), GETDATE(),20) 
                            from  TmReplenDay where dr=0 and TmMonthCode=@Code and (LeaderShip<>'')  group by LeaderShip;
                            insert into FapMessage (Fid,SEmpUid, REmpUid,SendTime,URL,Title,MsgContent,MsgCategory,EnableDate,DisableDate,Ts,Dr,CreateBy,CreateName,CreateDate) 
                            select dbo.GetFid(),@SEmpUid, EmpUid,@SendTime,@PUrl,
                            '考勤补签',
                            '您本月有'+cast(count(0) as varchar)+'条考勤异常需处理，详情进入系统查看。',
                            'Notice',
                            CONVERT(varchar(100), GETDATE(), 20),'9999-12-31 23:59:59',dbo.GetTs(),0,@UserFid,@UserName,CONVERT(varchar(100), GETDATE(),20)
                            from   TmReplenDay where dr=0 and TmMonthCode=@Code group by EmpUid;
                            insert into FapMessage (Fid,SEmpUid, REmpUid,SendTime,URL,Title,MsgContent,MsgCategory,EnableDate,DisableDate,Ts,Dr,CreateBy,CreateName,CreateDate)
                            select dbo.GetFid(),@SEmpUid,LeaderShip,@SendTime,@MUrl,
                            '考勤补签',
                            '您的直属下级本月有'+cast(count(0) as varchar)+'条考勤异常需处理，详情进入系统查看。',
                            'Notice',
                            CONVERT(varchar(100), GETDATE(), 20),'9999-12-31 23:59:59',dbo.GetTs(),0,@UserFid,@UserName,CONVERT(varchar(100), GETDATE(),20)
                            from   TmReplenDay where dr=0 and TmMonthCode=@Code group by LeaderShip;";

                _dbContext.Execute(sql, new DynamicParameters(new { SEmpUid = UserFid, Code = tmmonth.Code, UserName = UserName, UserFid = UserFid, SendTime = DateTimeUtils.CurrentDateTimeStr, PUrl = baseUrl + "/Home/MainFrame#SelfService/Ess/TmMyReplenCard", MUrl = baseUrl + "/Home/MainFrame#Time/Time/TmReplenCard" }));

            }
            else if (_dbContext.DatabaseDialect == DatabaseDialectEnum.MYSQL)
            {
                sql = "";
            }


        }
        /// <summary>
        /// 补签打卡
        /// </summary>
        /// <param name="empUid">人员Fid</param>
        /// <param name="empCode">人员Code</param>
        /// <param name="startTime">打卡开始时间</param>
        /// <param name="endTime">打卡结束时间</param>
        [Transactional]
        public void ReCard(string empUid, string empCode, string startTime, string endTime)
        {

            dynamic fdo1 = new FapDynamicObject(_dbContext.Columns("TmCardRecord"));
            fdo1.EmpUid = empUid;
            fdo1.CardTime = startTime;
            fdo1.EmpCode = empCode;
            fdo1.DeviceName = "补签数据";

            dynamic fdo2 = new FapDynamicObject(_dbContext.Columns("TmCardRecord"));
            fdo2.EmpUid = empUid;
            fdo2.CardTime = endTime;
            fdo2.EmpCode = empCode;
            fdo2.DeviceName = "补签数据";

            _dbContext.InsertDynamicData(fdo1);
            _dbContext.InsertDynamicData(fdo2);
            string cardDate = startTime.Substring(0, 10);
            _dbContext.Execute("update TmReplenDay set ReplenStatus = 1 where EmpUid=@EmpUid and CurrDate=@CurrDate", new DynamicParameters(new { EmpUid = empUid, CurrDate = cardDate }));



        }
        /// <summary>
        ///  批量补签打卡
        /// </summary>
        /// <param name="recard"></param>
        [Transactional]
        public void BatchPatchCard(IList<string> deptUids, string startDate, string endDate)
        {
            DynamicParameters param = new DynamicParameters();
            param.Add("StartDate", startDate);
            param.Add("EndDate", endDate);
            param.Add("DeptUids", deptUids);
            string sql = "select * from TmScheduleEmployee where DeptUid in @DeptUids and EndDate>@StartDate and StartDate<@EndDate";
            var scheduleEmployes = _dbContext.Query<TmScheduleEmployee>(sql, param);
            var scheduleUids = scheduleEmployes.Select(s => s.ScheduleUid).Distinct();
            if (!scheduleUids.Any())
            {
                throw new FapException("未找到排班信息");
            }
            param.Add("ScheduleUids", scheduleUids);
            string sqlWhere = "WorkDay>=@StartDate and WorkDay<=@EndDate and ScheduleUid in @ScheduleUids";
            var schedules = _dbContext.QueryWhere<TmSchedule>(sqlWhere, param).GroupBy(s => s.ScheduleUid);
            IList<TmCardRecord> cardList = new List<TmCardRecord>();
            foreach (var schedule in schedules)
            {
                foreach (var emp in scheduleEmployes.Where(s => s.ScheduleUid == schedule.Key))
                {
                    foreach (var sch in schedule)
                    {
                        TmCardRecord cdr = new TmCardRecord();
                        cdr.CardTime = sch.StartTime;
                        cdr.EmpUid = emp.EmpUid;
                        cdr.DeptUid = emp.DeptUid;
                        cdr.DeviceName = "批量补签";
                        cdr.DeviceNumber = "-";
                        cdr.IpAddress = "0.0.0.0";
                        cardList.Add(cdr);
                        TmCardRecord cdr1 = new TmCardRecord();
                        cdr1.CardTime = sch.StartTime;
                        cdr1.EmpUid = emp.EmpUid;
                        cdr1.DeptUid = emp.DeptUid;
                        cdr1.DeviceName = "批量补签";
                        cdr1.DeviceNumber = "-";
                        cdr1.IpAddress = "0.0.0.0";
                        cardList.Add(cdr1);
                    }
                }
            }
            _dbContext.InsertBatch(cardList);

        }
    }
}
