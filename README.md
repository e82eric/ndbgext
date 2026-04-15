# ndbgext

A WinDbg extension for .NET debugging. Load it with `.load <path>\ndbgext.dll`, then invoke commands with `!til.<command>`.

Run `!til.help` for a command summary, or `!til.help <command>` for detailed help on a specific command.

## Commands

### Thread and Stack Analysis

#### clruniqstack

Groups threads by their unique call stacks. Threads with identical stack traces (compared by metadata token) are collapsed into a single entry with a thread count.

```
!til.clruniqstack
```

For each unique stack, shows the frames (StackPointer, InstructionPointer, method name, metadata token) followed by the count and list of thread IDs (OS and managed) sharing that stack. Results are sorted by thread count ascending.

---

#### taskcallstack

Displays async/await logical call chains by walking task continuations and state machine relationships.

```
!til.taskcallstack
```

Extracts `AsyncMethodBuilderCore+MoveNextRunner` instances from the heap, chains state machines based on task continuations, and groups tasks by their full logical call context. Also detects `JoinableTask` dependencies and UI thread blocking (Microsoft.VisualStudio.Threading). Output shows the logical call chain, the number of tasks with identical chains, and their addresses.

---

#### blockinginfo

Identifies threads that are blocked on synchronization primitives and shows what they are waiting on.

```
!til.blockinginfo
```

Detects the following lock types:

- `ReaderWriterLock` (AcquireWriterLockInternal, AcquireReaderLockInternal, etc.)
- `ReaderWriterLockSlim` (TryEnterReadLock, TryEnterWriteLock, etc.)
- `Monitor` (Wait, Enter, TryEnter, etc.)
- `WaitHandle` (WaitOne, WaitAll, WaitAny)
- `Thread.Join`

For each blocking scenario, finds the lock object on the stack and groups results by frame, locking frame, lock type, and thread IDs.

---

### Tasks

#### tasks (tks)

Enumerates all `System.Threading.Tasks.Task` objects on the heap, grouped by state and method.

```
!til.tasks
!til.tasks -detail
!til.tasks -detail Running
```

| Option | Description |
|--------|-------------|
| `-detail` | Show per-task details (address, state, method, state machine type) |
| `[state]` | With `-detail`, filter by task state (e.g. `Running`, `Completed`) |

Summary mode groups tasks by state, by method, and by method-state cross-tabulation. Detail mode shows individual task addresses, state, method, and continuation information.

---

### Collection Inspection

#### dumpconcurrentdict (dcd)

Dumps the contents of a `ConcurrentDictionary<TKey, TValue>` instance.

```
!til.dumpconcurrentdict <address>
!til.dcd <address>
!til.dcd -nodes <address>
```

| Option | Description |
|--------|-------------|
| `-nodes` | Only output node addresses, one per line (omit key/value metadata) |

For each entry, displays the node address, key (type, address, value), and value (type, address, value). Supports both .NET Core and .NET Framework internal structures. Handles primitive types (int, long, bool, double, etc.) and object references.

---

#### dumpconcurrentqueue (dcq)

Dumps the items in a `ConcurrentQueue<T>` instance.

```
!til.dumpconcurrentqueue <address>
!til.dcq <address>
```

Walks the internal segment chain and displays each item's address, type name, and value. Supports both .NET Core and .NET Framework internal structures, and handles both object references and value types.

---

### Method and Type Inspection

#### getmetodname (gmn)

Resolves a native instruction pointer to its managed method name.

```
!til.getmetodname <instructionPointer>
!til.gmn <instructionPointer>
```

Returns the type name and method name for the method containing the given instruction pointer address.

---

#### decompilemethod

Decompiles a managed method to C# source using ILSpy.

```
!til.decompilemethod -sp <stackPointer>
!til.decompilemethod -ip <instructionPointer>
!til.decompilemethod -md <methodDesc>
```

| Mode | Description |
|------|-------------|
| `-sp <stackPointer>` | Find the method at the given stack pointer on any thread. Highlights the currently executing source line based on the IL offset, prints surrounding stack frames (`>>` marks current), and lists objects found in the frame's local data (MethodTable, Address, Type). |
| `-ip <instructionPointer>` | Decompile the method containing the given native instruction pointer. |
| `-md <methodDesc>` | Decompile the method identified by its MethodDesc address (from `!dumpmd` or `!clrstack`). |

---

#### clrstacksource

Walks managed stacks and prints decompiled C# source for each frame.
Defaults to the debugger's current thread when `-tid` is omitted.

```
!til.clrstacksource
!til.clrstacksource -tid <osThreadIdHex>
!til.clrstacksource -frames <start-end>
!til.clrstacksource -frames <start-end> -frameData
```

| Option | Description |
|--------|-------------|
| `-tid <osThreadIdHex>` | Restrict output to a single OS thread id. Accepts plain hex or `0x`-prefixed values. |
| `-frames <start-end>` | Decompile only an inclusive zero-based frame range while still printing the full stack. A single frame number is also accepted. |
| `-frameData` | Print stack parameters/variables discovered in the frame's stack range. |

For each matching thread, prints the thread id and the full stack with zero-based frame indices. When `-frames` is supplied, only frames in that range include decompiled source and optional frame data; frames outside the range are still listed without source. When IL-to-source mapping is available, the currently executing line is marked in the decompiled output. With `-frameData`, the command also prints object references found in that frame's stack range as a raw view of likely parameters/locals. Native/runtime frames are shown with a placeholder instead of source.

---

#### decompiletype

Decompiles an entire managed type to C# source using ILSpy.

```
!til.decompiletype <address>
!til.decompiletype -ad <address>
!til.decompiletype -nm <typeName>
!til.decompiletype -ip <instructionPointer>
!til.decompiletype -md <metadataToken>
!til.decompiletype -mt <methodTable>
```

| Mode | Description |
|------|-------------|
| `<address>` | Decompile the type of the object at the given heap address (shorthand for `-ad`). |
| `-ad <address>` | Decompile the type of the object at the given heap address. |
| `-nm <typeName>` | Decompile by fully-qualified type name (e.g. `MyNamespace.MyClass`). |
| `-ip <instructionPointer>` | Decompile the declaring type of the method at the given instruction pointer. |
| `-md <metadataToken>` | Decompile the type identified by its metadata token (decimal or `0x` hex). |
| `-mt <methodTable>` | Decompile the type identified by its method table address. |

---

#### types

Finds types that implement a given interface or extend a given base type.

```
!til.types -implements <interfaceName>
!til.types -short -implements <interfaceName>
```

| Option | Description |
|--------|-------------|
| `-implements <name>` | Required. The interface or base type name to search for. |
| `-short` | Show only the implementing type name (omit the interface name). |

Without `-short`, output is `TypeName : InterfaceName`. With `-short`, output is just `TypeName`.

---

### Module Extraction

#### savemodule

Extracts a loaded module from the target process and saves it to disk.

```
!til.savemodule <modulename>
```

Searches loaded modules for one containing `<modulename>` as a substring (case-sensitive). Requires exactly one match. The module is extracted from process memory and saved to the user's home directory (`%USERPROFILE%\<filename>`).

---

### Memory Analysis — Object Graph (Dominator Tree)

These commands use a dominator tree computed via the Lengauer-Tarjan algorithm. The dominator tree gives precise retained-size semantics: node A dominates node B if every path from the GC roots to B passes through A, meaning if A were collected, everything it dominates would also become collectible.

Run `buildobjectgraph` first, then use the analysis commands.

#### buildobjectgraph

Builds a whole-heap object reference graph, computes the dominator tree, and calculates per-type retained sizes. Results are cached in memory.

```
!til.buildobjectgraph
```

What it computes:
1. **Object graph** -- every managed object as a node, references as edges.
2. **Dominator tree** -- computed via Lengauer-Tarjan. Node A dominates node B if every path from the GC roots to B passes through A.
3. **Retained sizes** -- per-type minimum retained bytes derived from the dominator tree.

After building, prints: node count, edge count, type count, total size in bytes, and retained summary count.

This can be slow and memory-intensive on large heaps.

---

#### retainedbytestat

Displays per-type retained byte statistics from the dominator tree.

**Requires:** `buildobjectgraph`

```
!til.retainedbytestat
!til.retainedbytestat --min-retained-bytes 0
!til.retainedbytestat --min-retained-bytes 10000000
```

| Option | Description |
|--------|-------------|
| `--min-retained-bytes N` | Only show types retaining at least N bytes. Default: 1048576 (1 MB). |

Output columns:

| Column | Description |
|--------|-------------|
| Min Retained | Minimum retained bytes -- the memory that would become collectible if all instances of this type were removed. |
| Bytes | Exclusive (shallow) size of all instances of the type. |
| Type | Fully-qualified type name. |

Results are sorted by Min Retained descending, then Bytes descending.

---

#### referredfrom

Shows which parent types hold direct references to instances of the given type, ranked by total referenced bytes.

**Requires:** `buildobjectgraph`

```
!til.referredfrom <TypeName>
!til.referredfrom <TypeName> --top 20
```

| Option | Description |
|--------|-------------|
| `--top N` | Number of parent types to show. Default: 10. |

Type matching: first attempts an exact (case-insensitive) match on the fully-qualified type name. If no exact match is found, falls back to a substring (contains) match.

Output columns:

| Column | Description |
|--------|-------------|
| Bytes | Total shallow size of matched objects referenced by this parent type. |
| Count | Number of references from instances of this parent type. |
| ParentType | Fully-qualified parent type name. |

---

#### refferedtotree

Prints a tree of outgoing references from instances of the given type, expanding child types level by level.

**Requires:** `buildobjectgraph`

```
!til.refferedtotree <TypeName>
!til.refferedtotree <TypeName> --levels 5
```

| Option | Description |
|--------|-------------|
| `--levels N` | Depth of the tree to expand. Default: 3. |

Starting from all reachable objects matching `<TypeName>`, walks outgoing references in the object graph, aggregating children by type at each level. Avoids cycles by tracking visited nodes across the path.

Type matching: first attempts an exact (case-insensitive) match, then falls back to substring match.

Output columns:

| Column | Description |
|--------|-------------|
| Bytes | Total shallow size of child objects at this level. |
| Refs | Number of child references at this level. |
| Type | Child type name (indented by depth). |

Results at each level are sorted by Bytes descending, then Refs descending.

---

### Query and Utilities

#### tquery

A SQL-like query language for inspecting managed objects on the heap. Supports selecting fields, filtering with predicates, and traversing nested references.

```
!til.tquery [-short] [-debug] (-mt|-addr|-array|-implements) <address> (select <fields> [where <expr>] | where <expr>)
```

**Source modes:**

| Mode | Description |
|------|-------------|
| `-mt <methodTable>` | Query all heap objects with the given method table. |
| `-addr <address>` | Query a single object at the given address. |
| `-array <address>` | Query elements of the array at the given address. |
| `-implements <typeName>` | Query all objects whose type implements/extends the given type name. |

**Clauses:**

| Clause | Description |
|--------|-------------|
| `select <f1,f2,...>` | Project specific fields from each matching object. |
| `where <predicate>` | Filter objects matching the predicate. |
| `select ... where ...` | Combine projection and filtering. |

**Field expressions:**

| Expression | Description |
|------------|-------------|
| `fieldName` | Direct field on the object. |
| `field1.field2` | Nested field traversal (follows references and value types). |
| `*` | All fields on the object. |
| `field1.*` | All fields on a nested object. |
| `$this` | The value of the object itself. |

**Where predicate operators:**

| Operator | Description |
|----------|-------------|
| `==` | Equality |
| `!=` | Inequality |
| `>` | Greater than |
| `>=` | Greater than or equal |
| `<` | Less than |
| `<=` | Less than or equal |
| `=~` | Regex match (strings only) |
| `and` | Combine multiple predicates (all must match) |

Values must be single-quoted. Use `''` or `\'` to escape quotes inside values.

**Supported field types:** String, Boolean, Guid, DateTime, DateTimeOffset, Int16, Int32, Int64, Double, Float, Class (address), Struct (address).

**Flags:**

| Flag | Description |
|------|-------------|
| `-short` | Print values only (no address headers or field names). |
| `-debug` | Print debug/diagnostic output. |

**Examples:**

```
!til.tquery -mt 00007ff8a1234560 select _name,_id
!til.tquery -mt 00007ff8a1234560 where _status == '1'
!til.tquery -mt 00007ff8a1234560 select _name where _status > '0' and _active == 'True'
!til.tquery -addr 0000020fa1234560 select *
!til.tquery -array 0000020fa1234560 select _value where _key =~ 'foo.*'
!til.tquery -implements MyNamespace.IMyInterface select _name
!til.tquery -short -mt 00007ff8a1234560 select _name
```

---

#### tstore

Stores and retrieves the output of debugger commands in memory for later use.

```
!til.tstore -put <variable> -command <command>
!til.tstore -get <variable>
```

| Mode | Description |
|------|-------------|
| `-put <var> -command <cmd>` | Execute `<cmd>`, capture its output, and store it under `<var>`. |
| `-get <var>` | Print the previously stored output for `<var>`. |

The stored values persist for the lifetime of the debugging session. Useful for capturing command output and referencing it later.

**Examples:**

```
!til.tstore -put myheap -command !dumpheap -type System.String
!til.tstore -get myheap
```
