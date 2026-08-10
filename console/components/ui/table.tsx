import * as React from "react";

import { cn } from "@/lib/utils";

/**
 * A table, for wide viewports only.
 *
 * ⚠️ **Nothing here is responsive on its own, deliberately.** A fourteen-column portfolio cannot be made to work
 * at 320 px by any amount of CSS on the table — it becomes a horizontal scroll of numbers with the cabinet's
 * name off screen, which is the defect `CardList` exists to remove. So the caller renders this **above**
 * `lg:` and a card list below it, as two separate trees; `check:responsive`'s `card-fallback` rule counts one
 * `<CardList>` per `<Table>` for exactly that reason.
 *
 * The `overflow-x-auto` wrapper is still here as the last line of defence: a tablet in a narrow split view can
 * land just above the breakpoint with a long cabinet name, and a table that scrolls **inside its own container**
 * is the § 1 rule — the page body must never scroll sideways.
 */
function Table({ className, ...props }: React.ComponentProps<"table">) {
  return (
    <div data-slot="table-container" className="relative w-full overflow-x-auto">
      <table data-slot="table" className={cn("w-full caption-bottom text-sm", className)} {...props} />
    </div>
  );
}

function TableHeader({ className, ...props }: React.ComponentProps<"thead">) {
  return <thead data-slot="table-header" className={cn("[&_tr]:border-b", className)} {...props} />;
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  return <tbody data-slot="table-body" className={cn("[&_tr:last-child]:border-0", className)} {...props} />;
}

function TableRow({ className, ...props }: React.ComponentProps<"tr">) {
  return (
    <tr
      data-slot="table-row"
      className={cn("border-b border-border transition-colors hover:bg-muted/50", className)}
      {...props}
    />
  );
}

function TableHead({ className, ...props }: React.ComponentProps<"th">) {
  return (
    <th
      data-slot="table-head"
      className={cn(
        "h-10 whitespace-nowrap px-3 text-left align-middle text-xs font-medium text-muted-foreground",
        className,
      )}
      {...props}
    />
  );
}

function TableCell({ className, ...props }: React.ComponentProps<"td">) {
  return <td data-slot="table-cell" className={cn("px-3 py-2.5 align-middle", className)} {...props} />;
}

export { Table, TableHeader, TableBody, TableRow, TableHead, TableCell };
