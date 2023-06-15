import { cookies } from 'next/headers';
import { db } from "./db";

export default async function Page() {
  // Force dynamic rendering by observing request cookies
  const c = cookies();
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
