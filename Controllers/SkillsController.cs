using AiApi.Controllers;
using AiApi.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using System;
using TangYuan.Models;
using WebApi.Tools;
using System.Collections.Generic;

namespace TangYuan.Controllers
{
    /// <summary>
    /// AI 技能指令库控制器（方案 B：最省 token + AI 智能理解版）
    /// </summary>
    public class SkillsController : BaseCommandController
    {
        private readonly TokenCacheService _tokenCache;
        private readonly IWebHostEnvironment _env;

        public SkillsController(IConfiguration configuration, ILogger<AuthorizationController> logger)
            : base(configuration, logger)
        {
        }

        // ==========================================
        // 【AI 核心接口 1】获取所有可用指令名称（仅返回字符串，最小体积）
        // ==========================================
        /// <summary>
        /// AI专用：获取所有指令名称列表（极省 token）
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetAllSkillCodes()
        {
            try
            {
                var sql = "SELECT SkillCode FROM Skills ORDER BY ID ASC";
                var codeList = await QueryAsync<string>(sql);
                return Ok(ResponseHelper.Success(codeList));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取指令列表失败");
                return Ok(ResponseHelper.Fail<object>("获取指令列表失败"));
            }
        }

        // ==========================================
        // 【AI 核心接口 2】获取指令详情（仅返回动作，极简）
        // ==========================================
        /// <summary>
        /// AI 核心：根据指令名称获取执行动作（仅返回动作JSON）
        /// </summary>
        /// <param name="skillCode">指令名称</param>
        [HttpPost]
        public async Task<IActionResult> GetSkillAction([FromBody] string skillCode)
        {
            try
            {
                var sql = @"
                    SELECT SkillActions
                    FROM Skills 
                    WHERE SkillCode = @SkillCode 
                    LIMIT 1";

                DynamicParameters dp = new DynamicParameters();
                dp.Add("@SkillCode", skillCode);

                var skillActions = await QueryFirstOrDefaultAsync<string>(sql, dp);

                if (string.IsNullOrEmpty(skillActions))
                {
                    return Ok(ResponseHelper.Fail<object>("未找到匹配指令"));
                }

                return Ok(ResponseHelper.Success(skillActions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能动作失败");
                return Ok(ResponseHelper.Fail<object>("获取技能失败：" + ex.Message));
            }
        }

        // ==========================================
        // 【AI 核心接口 3】【方案 B 专用】获取指令 + 说明（让 AI 知道指令用途）
        // ==========================================
        /// <summary>
        /// AI专用：获取所有指令及其简单说明（极省 token，辅助 AI 理解指令功能）
        /// 返回格式：[{SkillCode:"指令名", Desc:"说明"}]
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetSkillListForAI()
        {
            try
            {
                // 只查 指令名 + 说明（Other 字段存放说明）
                // 如果你没有填 Other，这里会返回 null，不影响
                var sql = @"
                    SELECT 
                        SkillCode, 
                        Other AS AIDesc 
                    FROM Skills 
                    ORDER BY ID ASC";

                // 动态查询，返回匿名对象
                var aiSkillList = await QueryAsync<dynamic>(sql);
                return Ok(ResponseHelper.Success(aiSkillList));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取AI技能说明列表失败");
                return Ok(ResponseHelper.Fail<object>("获取列表失败"));
            }
        }

        // ==========================================
        // 【管理接口】保存/新增技能（有ID则更新，无ID则新增）
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> SaveSkillAction([FromBody] SkillModel model)
        {
            try
            {
                int result = 0;
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                if (model.ID == 0)
                {
                    // 新增
                    var insertSql = @"
                        INSERT INTO Skills (
                            SkillCode, SkillActions, SkillType, NeedLogin, 
                            UpdateTime, WebSiteName, WebSiteUrl, Other
                        ) 
                        VALUES (
                            @SkillCode, @SkillActions, @SkillType, @NeedLogin,
                            @UpdateTime, @WebSiteName, @WebSiteUrl, @Other
                        )";

                    DynamicParameters dp = new DynamicParameters();
                    dp.Add("@SkillCode", model.SkillCode);
                    dp.Add("@SkillActions", model.SkillActions);
                    dp.Add("@SkillType", model.SkillType);
                    dp.Add("@NeedLogin", model.NeedLogin);
                    dp.Add("@UpdateTime", now);
                    dp.Add("@WebSiteName", model.WebSiteName);
                    dp.Add("@WebSiteUrl", model.WebSiteUrl);
                    dp.Add("@Other", model.Other); // 这里填说明，给AI用

                    result = await ExecuteAsync(insertSql, dp);
                }
                else
                {
                    // 更新
                    var updateSql = @"
                        UPDATE Skills SET 
                            SkillCode = @SkillCode,
                            SkillActions = @SkillActions,
                            SkillType = @SkillType,
                            NeedLogin = @NeedLogin,
                            UpdateTime = @UpdateTime,
                            WebSiteName = @WebSiteName,
                            WebSiteUrl = @WebSiteUrl,
                            Other = @Other
                        WHERE ID = @ID";

                    DynamicParameters dp = new DynamicParameters();
                    dp.Add("@ID", model.ID);
                    dp.Add("@SkillCode", model.SkillCode);
                    dp.Add("@SkillActions", model.SkillActions);
                    dp.Add("@SkillType", model.SkillType);
                    dp.Add("@NeedLogin", model.NeedLogin);
                    dp.Add("@UpdateTime", now);
                    dp.Add("@WebSiteName", model.WebSiteName);
                    dp.Add("@WebSiteUrl", model.WebSiteUrl);
                    dp.Add("@Other", model.Other);

                    result = await ExecuteAsync(updateSql, dp);
                }

                return result > 0
                    ? Ok(ResponseHelper.Success("保存成功"))
                    : Ok(ResponseHelper.Fail<object>("保存失败"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存技能失败");
                return Ok(ResponseHelper.Fail<object>("保存失败：" + ex.Message));
            }
        }

        // ==========================================
        // 【管理接口】获取技能列表（管理后台用）
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> GetSkillList()
        {
            try
            {
                var sql = "SELECT * FROM Skills ORDER BY ID DESC";
                var list = await QueryAsync<dynamic>(sql);
                return Ok(ResponseHelper.Success(list));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能列表失败");
                return Ok(ResponseHelper.Fail<object>("获取列表失败"));
            }
        }

        // ==========================================
        // 【管理接口】删除技能
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> DeleteSkill([FromBody] int id)
        {
            try
            {
                var sql = "DELETE FROM Skills WHERE ID = @ID";
                DynamicParameters dp = new DynamicParameters();
                dp.Add("@ID", id);
                int result = await ExecuteAsync(sql, dp);

                return result > 0
                    ? Ok(ResponseHelper.Success("删除成功"))
                    : Ok(ResponseHelper.Fail<object>("删除失败"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除技能失败");
                return Ok(ResponseHelper.Fail<object>("删除失败：" + ex.Message));
            }
        }

        // ==========================================
        // 【管理接口】通用执行SQL（谨慎使用）
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> ExecSql([FromBody] string sqlText)
        {
            try
            {
                int result = await ExecuteAsync(sqlText);
                return Ok(ResponseHelper.Success($"执行成功，受影响行数：{result}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行SQL失败：" + sqlText);
                return Ok(ResponseHelper.Fail<object>("执行失败：" + ex.Message));
            }
        }
    }

    // ==========================================
    // 技能实体模型
    // ==========================================
    public class SkillModel
    {
        public int ID { get; set; }
        public string SkillCode { get; set; }
        public string SkillActions { get; set; }
        public string SkillType { get; set; }
        public int NeedLogin { get; set; }
        public string WebSiteName { get; set; }
        public string WebSiteUrl { get; set; }
        public string Other { get; set; } // 【关键】这个字段用来存 AI 的指令说明！
    }
}