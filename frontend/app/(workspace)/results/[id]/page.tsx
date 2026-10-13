import { ResultsView } from "@/components/results/results-view";

// /results/[id] page: reads the scan id from the URL and shows that saved scan. Stores nothing.
export default async function ResultPage({ params }: PageProps<"/results/[id]">) {
  const { id } = await params;
  return <ResultsView id={id} />;
}
