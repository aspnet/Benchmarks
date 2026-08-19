DROP DATABASE IF EXISTS [hello_world];
CREATE DATABASE [hello_world];
GO

USE [hello_world];
GO

DROP TABLE IF EXISTS [world];
CREATE TABLE  [world] (
  [id] INT IDENTITY (1, 1) NOT NULL,
  [randomNumber] INT NOT NULL DEFAULT 0,
  PRIMARY KEY  (id)
)
GO

DECLARE @cnt INT = 0;
DECLARE @max INT = 10000;

WHILE @cnt < @max
BEGIN
	INSERT INTO [world] ([randomNumber]) VALUES ( (ABS(CHECKSUM(NewId())) % 10000) + 1 );
	SET @cnt = @cnt + 1;
END;
GO


DROP TABLE IF EXISTS [fortune];
CREATE TABLE  [fortune] (
  [id] INT IDENTITY (1, 1) NOT NULL,
  [message] NVARCHAR(2048) NOT NULL,
  PRIMARY KEY  (id)
)
GO

INSERT INTO [fortune] ([message]) VALUES (N'fortune: No such file or directory');
INSERT INTO [fortune] ([message]) VALUES (N'A computer scientist is someone who fixes things that aren''t broken.');
INSERT INTO [fortune] ([message]) VALUES (N'After enough decimal places, nobody gives a damn.');
INSERT INTO [fortune] ([message]) VALUES (N'A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1');
INSERT INTO [fortune] ([message]) VALUES (N'A computer program does what you tell it to do, not what you want it to do.');
INSERT INTO [fortune] ([message]) VALUES (N'Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen');
INSERT INTO [fortune] ([message]) VALUES (N'Any program that runs right is obsolete.');
INSERT INTO [fortune] ([message]) VALUES (N'A list is only as strong as its weakest link. — Donald Knuth');
INSERT INTO [fortune] ([message]) VALUES (N'Feature: A bug with seniority.');
INSERT INTO [fortune] ([message]) VALUES (N'Computers make very fast, very accurate mistakes.');
INSERT INTO [fortune] ([message]) VALUES (N'<script>alert("This should not be displayed in a browser alert box.");</script>');
INSERT INTO [fortune] ([message]) VALUES (N'フレームワークのベンチマーク');
GO

PRINT "Data imported"