import { db } from "./db";

export default async function Page() {
  const data = await db.getFortunes();
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
