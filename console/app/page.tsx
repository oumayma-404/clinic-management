import { redirect } from "next/navigation";

/** The console has one destination; « / » is not a screen. */
export default function Home() {
  redirect("/cabinets");
}
