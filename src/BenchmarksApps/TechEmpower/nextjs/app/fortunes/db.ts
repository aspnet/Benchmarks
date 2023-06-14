import { Pool } from "pg";
import { Fortune } from "./fortune";
import { env } from "process";

const pool = new Pool({
    user: "benchmarkdbuser",
    password: "benchmarkdbpass",
    database: "hello_world",
    host: env["DB_HOST"] ?? "localhost"
});

const queries = {
    fortunes: {
        name: "get-fortunes",
        text: "SELECT * FROM fortune"
    }
};

async function getFortunes() {
    const res = await db.pool.query(queries.fortunes);
    var fortunes = res.rows.map(r => new Fortune(r.id, r.message));
    fortunes.push(new Fortune(0, "Additional fortune added at request time."));
    fortunes.sort((a, b) => a.message.localeCompare(b.message));
    return fortunes;
}

async function getFortunesNoDb() {
    var fortunes = [
        new Fortune(1, "fortune: No such file or directory"),
        new Fortune(2, "A computer scientist is someone who fixes things that aren't broken."),
        new Fortune(3, "After enough decimal places, nobody gives a damn."),
        new Fortune(4, "A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1"),
        new Fortune(5, "A computer program does what you tell it to do, not what you want it to do."),
        new Fortune(6, "Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen"),
        new Fortune(7, "Any program that runs right is obsolete."),
        new Fortune(8, "A list is only as strong as its weakest link. — Donald Knuth"),
        new Fortune(9, "Feature: A bug with seniority."),
        new Fortune(10, "Computers make very fast, very accurate mistakes."),
        new Fortune(11, "<script>alert(\"This should not be displayed in a browser alert box.\");</script>"),
        new Fortune(12, "フレームワークのベンチマーク"),
        new Fortune(0, "Additional fortune added at request time.")
    ];
    fortunes.sort((a, b) => a.message.localeCompare(b.message));
    return fortunes;
}

export const db = {
    pool: pool,
    getFortunes: getFortunes,
    getFortunesNoDb: getFortunesNoDb
};