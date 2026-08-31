/**
 * The parts of the File System Access API that `lib/vault/` uses and TypeScript's DOM lib does not declare.
 *
 * `FileSystemDirectoryHandle`, `FileSystemFileHandle` and `FileSystemWritableFileStream` are already in
 * `lib.dom`; what is missing is the **picker** on `Window` and the two **permission** methods, which are still
 * outside the standard even though every Chromium browser ships them. Declared here rather than pulled in as a
 * `@types` package: three members, and a dependency for them would be larger than the file.
 */

type FileSystemPermissionState = 'granted' | 'denied' | 'prompt'

interface FileSystemHandlePermissionDescriptor {
  mode?: 'read' | 'readwrite'
}

interface FileSystemHandle {
  queryPermission(descriptor?: FileSystemHandlePermissionDescriptor): Promise<FileSystemPermissionState>
  requestPermission(descriptor?: FileSystemHandlePermissionDescriptor): Promise<FileSystemPermissionState>
}

interface DirectoryPickerOptions {
  id?: string
  mode?: 'read' | 'readwrite'
  startIn?: FileSystemHandle | 'desktop' | 'documents' | 'downloads' | 'music' | 'pictures' | 'videos'
}

interface Window {
  showDirectoryPicker?(options?: DirectoryPickerOptions): Promise<FileSystemDirectoryHandle>
}
