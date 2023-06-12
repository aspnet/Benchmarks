import { FortuneRow } from "./fortune";

async function getData() {
    // TODO: Load this from the database
    var fortunes = [
        new FortuneRow(1, "fortune: No such file or directory"),
        new FortuneRow(2, "A computer scientist is someone who fixes things that aren't broken."),
        new FortuneRow(3, "After enough decimal places, nobody gives a damn."),
        new FortuneRow(4, "A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1"),
        new FortuneRow(5, "A computer program does what you tell it to do, not what you want it to do."),
        new FortuneRow(6, "Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen"),
        new FortuneRow(7, "Any program that runs right is obsolete."),
        new FortuneRow(8, "A list is only as strong as its weakest link. — Donald Knuth"),
        new FortuneRow(9, "Feature: A bug with seniority."),
        new FortuneRow(10, "Computers make very fast, very accurate mistakes."),
        new FortuneRow(11, "<script>alert(\"This should not be displayed in a browser alert box.\");</script>"),
        new FortuneRow(12, "フレームワークのベンチマーク"),
        new FortuneRow(0, "Additional fortune added at request time.")
    ];
    fortunes.sort((a, b) => a.message.localeCompare(b.message));
    return fortunes;
}

export default async function Page() {
  let data = await getData();
  return (
    <table>
        <tbody>
            <tr><th>id</th><th>message</th></tr>
            {data.map((row, idx) => 
                <tr key={idx}><td>{row.id}</td><td>{row.message}</td></tr>
            )}
        </tbody>
    </table>
  )
}
