BEGIN;

CREATE TABLE  World (
  id integer NOT NULL,
  randomNumber integer NOT NULL default 0,
  PRIMARY KEY  (id)
);
GRANT SELECT, UPDATE ON World to benchmarkdbuser;

INSERT INTO World (id, randomnumber)
SELECT x.id, floor(random() * 10000 + 1) FROM generate_series(1,10000) as x(id);

CREATE TABLE Fortune (
  id integer NOT NULL,
  message varchar(2048) NOT NULL,
  PRIMARY KEY  (id)
);
GRANT SELECT ON Fortune to benchmarkdbuser;

INSERT INTO Fortune (id, message) VALUES (1, 'fortune: No such file or directory');
INSERT INTO Fortune (id, message) VALUES (2, 'A computer scientist is someone who fixes things that aren''t broken.');
INSERT INTO Fortune (id, message) VALUES (3, 'After enough decimal places, nobody gives a damn.');
INSERT INTO Fortune (id, message) VALUES (4, 'A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1');
INSERT INTO Fortune (id, message) VALUES (5, 'A computer program does what you tell it to do, not what you want it to do.');
INSERT INTO Fortune (id, message) VALUES (6, 'Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen');
INSERT INTO Fortune (id, message) VALUES (7, 'Any program that runs right is obsolete.');
INSERT INTO Fortune (id, message) VALUES (8, 'A list is only as strong as its weakest link. — Donald Knuth');
INSERT INTO Fortune (id, message) VALUES (9, 'Feature: A bug with seniority.');
INSERT INTO Fortune (id, message) VALUES (10, 'Computers make very fast, very accurate mistakes.');
INSERT INTO Fortune (id, message) VALUES (11, '<script>alert("This should not be displayed in a browser alert box.");</script>');
INSERT INTO Fortune (id, message) VALUES (12, 'フレームワークのベンチマーク');

CREATE TABLE  "World" (
  id integer NOT NULL,
  randomNumber integer NOT NULL default 0,
  PRIMARY KEY  (id)
);
GRANT SELECT, UPDATE ON "World" to benchmarkdbuser;

INSERT INTO "World" (id, randomnumber)
SELECT x.id, floor(random() * 10000 + 1) FROM generate_series(1,10000) as x(id);

CREATE TABLE "Fortune" (
  id integer NOT NULL,
  message varchar(2048) NOT NULL,
  PRIMARY KEY  (id)
);
GRANT SELECT ON "Fortune" to benchmarkdbuser;

INSERT INTO "Fortune" (id, message) VALUES (1, 'fortune: No such file or directory');
INSERT INTO "Fortune" (id, message) VALUES (2, 'A computer scientist is someone who fixes things that aren''t broken.');
INSERT INTO "Fortune" (id, message) VALUES (3, 'After enough decimal places, nobody gives a damn.');
INSERT INTO "Fortune" (id, message) VALUES (4, 'A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1');
INSERT INTO "Fortune" (id, message) VALUES (5, 'A computer program does what you tell it to do, not what you want it to do.');
INSERT INTO "Fortune" (id, message) VALUES (6, 'Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen');
INSERT INTO "Fortune" (id, message) VALUES (7, 'Any program that runs right is obsolete.');
INSERT INTO "Fortune" (id, message) VALUES (8, 'A list is only as strong as its weakest link. — Donald Knuth');
INSERT INTO "Fortune" (id, message) VALUES (9, 'Feature: A bug with seniority.');
INSERT INTO "Fortune" (id, message) VALUES (10, 'Computers make very fast, very accurate mistakes.');
INSERT INTO "Fortune" (id, message) VALUES (11, '<script>alert("This should not be displayed in a browser alert box.");</script>');
INSERT INTO "Fortune" (id, message) VALUES (12, 'フレームワークのベンチマーク');


CREATE TABLE "RewriteRule" (
  id integer NOT NULL,
  regex varchar(2048) NOT NULL,
  replacement varchar(2048) NOT NULL,
  PRIMARY KEY  (id)
);

GRANT SELECT ON "RewriteRule" to benchmarkdbuser;

INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (1, 'rewrite1' ,'replacement1');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (2, 'rewrite1' ,'replacement2');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (3, 'rewrite1' ,'replacement3');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (4, 'rewrite1' ,'replacement4');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (5, 'rewrite1' ,'replacement5');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (6, 'rewrite1' ,'replacement6');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (7, 'rewrite1' ,'replacement7');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (8, 'rewrite1' ,'replacement8');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (9, 'rewrite1' ,'replacement9');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (10,'rewrite1' ,'replacement10');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (11,'rewrite1' ,'replacement11');
INSERT INTO "RewriteRule" (id, regex,replacement) VALUES (12,'rewrite1' ,'replacement12');



CREATE TABLE RewriteRule (
  id integer NOT NULL,
  regex varchar(2048) NOT NULL,
  replacement varchar(2048) NOT NULL,
  PRIMARY KEY  (id)
);
GRANT SELECT ON RewriteRule to benchmarkdbuser;

INSERT INTO RewriteRule (id, regex,replacement) VALUES (1, 'rewrite1' ,'replacement1');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (2, 'rewrite1' ,'replacement2');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (3, 'rewrite1' ,'replacement3');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (4, 'rewrite1' ,'replacement4');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (5, 'rewrite1' ,'replacement5');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (6, 'rewrite1' ,'replacement6');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (7, 'rewrite1' ,'replacement7');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (8, 'rewrite1' ,'replacement8');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (9, 'rewrite1' ,'replacement9');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (10,'rewrite1' ,'replacement10');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (11,'rewrite1' ,'replacement11');
INSERT INTO RewriteRule (id, regex,replacement) VALUES (12,'rewrite1' ,'replacement12');

COMMIT;