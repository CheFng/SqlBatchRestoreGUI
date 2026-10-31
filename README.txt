SqlBatchRestoreGUI v1.0.7
=========================

SQL Server BAK 批量还原工具

v1.0.7 新增：还原后自动清理事务日志
-----------------------------------
1. 新增“还原成功后自动清理日志”选项，默认关闭。
2. 可设置“每个 LDF 目标大小”，默认 1024 MB，允许 64 ~ 102400 MB。
3. 启用后，每个数据库还原成功后自动执行：
   - ALTER DATABASE [数据库] SET RECOVERY SIMPLE
   - CHECKPOINT
   - 对每个 LOG 文件执行 DBCC SHRINKFILE(..., 目标MB)
4. 运行日志会显示：
   - 清理前日志文件总分配大小
   - 清理后日志文件总分配大小
   - 约释放空间
   - 日志文件数量
5. 日志清理失败不会把已经成功还原的数据库判定为“还原失败”；表格会显示“还原成功，日志清理失败”，并在运行日志中记录具体原因。

重要说明
--------
“自动清理日志”会把数据库恢复模式改为 SIMPLE，会中断原有 FULL/BULK_LOGGED 日志备份链。
因此该选项默认关闭，建议仅用于本地、测试、开发、客户数据分析等不需要日志链的还原环境。
如果你需要保留完整恢复模式和日志备份链，请不要勾选此选项。

DBCC SHRINKFILE 的目标大小不是绝对保证值。如果存在活动日志/VLF 等 SQL Server 当前无法释放的内容，实际 LDF 可能仍大于目标值。
如果一个数据库有多个 LDF，“目标大小”是每个 LDF 的目标大小。

空间说明
--------
自动清理发生在 RESTORE 成功之后，因此还原期间仍必须先有足够空间创建 BAK 中记录的完整 MDF/NDF/LDF。
例如 BAK 中 LDF 定义为 51 GB，即使启用“还原后收缩到 1 GB”，RESTORE 时目标磁盘仍需要先容纳这 51 GB 日志文件；还原完成后才会尝试释放空间。

已有功能
--------
- 扫描指定目录中的多个 .bak
- 可扫描子目录
- 自动读取 BAK 内真实数据库名
- 自动选取最新完整数据库备份集
- 同名数据库先删除后还原
- RESTORING / RECOVERING 等残留状态数据库删除处理
- MDF/NDF/LDF 自动 MOVE 到目标目录
- 可自定义数据文件目录和日志文件目录
- 数据/日志目录留空时使用 SQL Server 默认目录
- 扫描时读取数据文件/日志文件逻辑大小
- 扫描时检查 SQL Server 主机目标磁盘可用空间
- 空间不足自动标红并默认取消勾选
- 真正还原前再次检查磁盘空间
- 超大 BAK：CommandTimeout=0
- sys.dm_exec_requests.percent_complete 实际还原进度
- 单个数据库失败后继续下一个
- 中止当前还原
- “测试连接”后保留已经扫描好的还原按钮状态
- 运行时单 EXE，不生成 Logs 目录，不生成 SqlBatchRestore.settings.xml
- 运行日志只显示在界面中，不落盘

生成 EXE
--------
1. 解压整个目录。
2. 双击 Build-EXE.bat。
3. 成功后生成 SqlBatchRestore.exe。
4. 实际使用或复制给其他电脑时，只需要 SqlBatchRestore.exe。

Build-EXE.bat 使用 Windows 自带 .NET Framework C# 编译器，不需要 Visual Studio，也不需要 NuGet 包。
源码继续保持兼容旧版 C# 5 编译器。

权限
----
建议使用具有 sysadmin 权限的 SQL Server 登录账号，以便执行 RESTORE、DROP DATABASE、ALTER DATABASE、CHECKPOINT、DBCC SHRINKFILE、查看服务器磁盘空间及终止占用会话。

BAK 可访问性
-------------
BAK 是由 SQL Server 服务进程读取的。
远程 SQL Server 场景下，本机 C:\Backup\a.bak 通常无法被服务器读取，应使用 SQL Server 服务账号可访问的共享路径（UNC）或服务器本地路径。
