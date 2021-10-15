using System;

namespace Uno.Wasm.MetadataUpdate
{
	internal sealed class UpdateDelta
	{
		public Guid ModuleId { get; set; }

		public byte[] MetadataDelta { get; set; } = default!;

		public byte[] ILDelta { get; set; } = default!;

		public int[]? UpdatedTypes { get; set; }
	}
}
