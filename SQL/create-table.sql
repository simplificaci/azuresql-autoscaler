DROP TABLE IF EXISTS [dbo].[AutoscalerNumbers]
GO

SELECT TOP (1000000)
	ROW_NUMBER() OVER (ORDER BY A.[object_id]) AS Number,
	RAND(CHECKSUM(NEWID())) AS Random
INTO
	[dbo].[AutoscalerNumbers]
FROM
	sys.[all_columns] a, sys.[all_columns] b
GO

CREATE CLUSTERED INDEX ixc ON dbo.[AutoscalerNumbers](Number)
GO