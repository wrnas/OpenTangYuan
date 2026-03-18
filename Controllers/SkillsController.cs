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
    /// AI 技能指令库控制器（用于管理和查询技能动作）
    /// </summary>
    public class SkillsController : BaseCommandController
    {
        private readonly TokenCacheService _tokenCache;
        private readonly IWebHostEnvironment _env;

        public SkillsController(IConfiguration configuration, ILogger<AuthorizationController> logger)
            : base(configuration, logger)
        {
        }

        /// <summary>
        /// AI专用：获取所有可用指令名称（极致省token）
        /// 只返回指令列表，无任何多余字段
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetAllSkillCodes()
        {
            try
            {
                // 只查 SkillCode，只查指令名称！！！
                var sql = "SELECT SkillCode FROM Skills ORDER BY ID ASC";

                // 直接返回 List<string> 指令列表，最小体积
                var codeList = await QueryAsync<string>(sql);

                // 返回纯字符串数组，最省token
                return Ok(ResponseHelper.Success(codeList));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取指令列表异常");
                return Ok(ResponseHelper.Fail<object>("获取指令列表失败"));
            }
        }


        /// <summary>
        /// AI 核心接口：根据用户指令，查询匹配的技能动作【仅返回动作，最省token】
        /// </summary>
        /// <param name="skillCode">用户指令关键词（如：打开淘宝、打开百度）</param>
        /// <returns>技能动作JSON（直接给插件执行）</returns>
        [HttpPost]
        public async Task<IActionResult> GetSkillAction([FromBody] string skillCode)
        {
            try
            {
                // ==========================================
                // 重点：只查 SkillActions，不返回任何多余字段！
                // 给AI用，token越少越好，速度越快越好
                // ==========================================
                var sql = @"
            SELECT SkillActions
            FROM Skills 
            WHERE SkillCode = @SkillCode 
            LIMIT 1";

                DynamicParameters dp = new DynamicParameters();
                dp.Add("@SkillCode", skillCode);

                // 只取动作字符串
                var skillActions = await QueryFirstOrDefaultAsync<string>(sql, dp);

                if (string.IsNullOrEmpty(skillActions))
                {
                    return Ok(ResponseHelper.Fail<object>("未找到匹配的技能指令"));
                }

                // 只返回动作，没有任何多余字段
                return Ok(ResponseHelper.Success(skillActions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能指令异常");
                return Ok(ResponseHelper.Fail<object>("获取技能失败：" + ex.Message));
            }
        }

        /// <summary>
        /// 保存/新增 技能指令（有ID则更新，无ID则新增）
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveSkillAction([FromBody] SkillModel model)
        {
            try
            {
                int result = 0;

                if (model.ID == 0)
                {
                    // ==========================================
                    // 新增技能
                    // ==========================================
                    var insertSql = @"
                        INSERT INTO Skills (
                            SkillCode, SkillActions, SkillType, NeedLogin, 
                            UpdateTime, WebSiteName, WebSiteUrl, Other
                        ) 
                        VALUES (
                            @SkillCode, @SkillActions, @SkillType, @NeedLogin,
                            @UpdateTime, @WebSiteName, @WebSiteUrl, @Other
                        )";

                    DynamicParameters insertDp = new DynamicParameters();
                    insertDp.Add("@SkillCode", model.SkillCode);
                    insertDp.Add("@SkillActions", model.SkillActions);
                    insertDp.Add("@SkillType", model.SkillType);
                    insertDp.Add("@NeedLogin", model.NeedLogin);
                    insertDp.Add("@UpdateTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    insertDp.Add("@WebSiteName", model.WebSiteName);
                    insertDp.Add("@WebSiteUrl", model.WebSiteUrl);
                    insertDp.Add("@Other", model.Other);

                    result = await ExecuteAsync(insertSql, insertDp);
                }
                else
                {
                    // ==========================================
                    // 更新技能
                    // ==========================================
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

                    DynamicParameters updateDp = new DynamicParameters();
                    updateDp.Add("@ID", model.ID);
                    updateDp.Add("@SkillCode", model.SkillCode);
                    updateDp.Add("@SkillActions", model.SkillActions);
                    updateDp.Add("@SkillType", model.SkillType);
                    updateDp.Add("@NeedLogin", model.NeedLogin);
                    updateDp.Add("@UpdateTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    updateDp.Add("@WebSiteName", model.WebSiteName);
                    updateDp.Add("@WebSiteUrl", model.WebSiteUrl);
                    updateDp.Add("@Other", model.Other);

                    result = await ExecuteAsync(updateSql, updateDp);
                }

                if (result > 0)
                {
                    return Ok(ResponseHelper.Success("保存成功"));
                }

                return Ok(ResponseHelper.Fail<object>("保存失败"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存技能异常");
                return Ok(ResponseHelper.Fail<object>("保存失败：" + ex.Message));
            }
        }

        /// <summary>
        /// 获取所有技能列表（用于管理后台）
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetSkillList()
        {
            try
            {
                var sql = @"SELECT * FROM Skills ORDER BY ID DESC";
                var list = await QueryAsync<dynamic>(sql);
                return Ok(ResponseHelper.Success(list));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能列表异常");
                return Ok(ResponseHelper.Fail<object>("获取列表失败"));
            }
        }

        /// <summary>
        /// 删除技能
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteSkill([FromBody] int id)
        {
            try
            {
                var sql = @"DELETE FROM Skills WHERE ID = @ID";
                DynamicParameters dp = new DynamicParameters();
                dp.Add("@ID", id);

                int result = await ExecuteAsync(sql, dp);

                if (result > 0)
                {
                    return Ok(ResponseHelper.Success("删除成功"));
                }

                return Ok(ResponseHelper.Fail<object>("删除失败"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除技能异常");
                return Ok(ResponseHelper.Fail<object>("删除失败：" + ex.Message));
            }
        }

        /// <summary>
        /// 通用执行SQL（谨慎使用）
        /// </summary>
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
                _logger.LogError(ex, "执行SQL异常：" + sqlText);
                return Ok(ResponseHelper.Fail<object>("执行失败：" + ex.Message));
            }
        }
    }

    // ==========================================
    // 技能实体模型（和数据表完全对应）
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
        public string Other { get; set; }
    }
}