import { json } from "@remix-run/node";
import { db } from "../models/db";
import { useLoaderData } from "@remix-run/react";

export const loader = async () => {
  const data = await db.getFortunes();
  return json({ data });
};

export default function Page() {
  const { data } = useLoaderData<typeof loader>();
  return (
    <table>
      <tbody>
        <tr>
          <th>id</th>
          <th>message</th>
        </tr>
        {data.map((row, idx) => (
          <tr key={idx}>
            <td>{row.id}</td>
            <td>{row.message}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
