import { db } from "./db";
import { Metadata } from 'next'

// Force page to render dynamically from the server every request
export const dynamic = "force-dynamic";
export const revalidate = 0;
export const metadata: Metadata = { viewport: {} };

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
