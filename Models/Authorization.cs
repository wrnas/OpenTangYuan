namespace TangYuan.Models
{

    /// <summary>
    /// 
    /// </summary>
    public class AICommandModel
    {
        /// <summary>
        /// 00:sql ; 01:普通命令 
        /// </summary>
        public string ?CommandType { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string ?CommandText { get; set; }
        public int CommandUid { get; set; }
        public int CommandMemo { get; set; }
    }

    public class LoginModel
    {
        public string UserCode { get; set; }
        public string Password { get; set; }
    }


    /// <summary>
    /// 用户信息 + 角色 的DTO，用于登录、权限查询等
    /// </summary>
    public class UserWithRolesDto
    {
        public int ID { get; set; }
        public string UserPassword { get; set; }
        public string UserDesc { get; set; }
        public string DeptIDs { get; set; }
        public bool IsAdmin { get; set; }
        public string UserCode { get; set; }
        public string UserName { get; set; }
        public string Station { get; set; }
        public string CellPhone { get; set; }
        public string Roles { get; set; }  // 多角色用逗号拼接
    }

    public class Acc_Function
    {
        public int FunctionID { get; set; }
        public string FunctionCode { get; set; }
        public string FunctionName { get; set; }
        public string Remark { get; set; }
    }

    public class Acc_Role
    {
        public int RoleId { get; set; }
        public string RoleCode { get; set; }
        public string RoleName { get; set; }
        public string Remark { get; set; }
    }

    public class Acc_RoseVsFunction
    {
        public int RFID { get; set; }
        public int? RoleID { get; set; }
        public int? FunctionID { get; set; }
    }

    public class Acc_UserVsRole
    {
        public int URID { get; set; }
        public int? UserID { get; set; }
        public int? RoleID { get; set; }
        public string Remark { get; set; }
    }


}
