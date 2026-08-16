import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../theme.dart';

class AddCatchScreen extends StatefulWidget {
  const AddCatchScreen({
    super.key,
    required this.spots,
    this.initialSpot,
    this.initialDate,
  });

  final List<Spot> spots;
  final Spot? initialSpot;
  final DateTime? initialDate;

  @override
  State<AddCatchScreen> createState() => _AddCatchScreenState();
}

class _AddCatchScreenState extends State<AddCatchScreen> {
  final _formKey = GlobalKey<FormState>();

  late Spot _spot;
  late DateTime _date;
  final _speciesCtrl = TextEditingController();
  final _weightCtrl = TextEditingController();
  final _lengthCtrl = TextEditingController();
  final _notesCtrl = TextEditingController();

  Uint8List? _photo;
  bool _uploading = false;
  String _photoUrl = '';

  bool _analyzing = false;
  List<FishIdPrediction> _predictions = [];
  String _analyzeError = '';

  @override
  void initState() {
    super.initState();
    _spot = widget.initialSpot ?? widget.spots.first;
    _date = widget.initialDate ?? DateTime.now();
  }

  @override
  void dispose() {
    _speciesCtrl.dispose();
    _weightCtrl.dispose();
    _lengthCtrl.dispose();
    _notesCtrl.dispose();
    super.dispose();
  }

  Future<void> _pickPhoto() async {
    final picked = await ImagePicker().pickImage(source: ImageSource.gallery);
    if (picked == null) return;
    final bytes = await picked.readAsBytes();
    if (!mounted) return;
    setState(() {
      _photo = bytes;
      _photoUrl = '';
      _predictions = [];
      _analyzeError = '';
    });
  }

  Future<void> _analyze() async {
    final photo = _photo;
    if (photo == null) return;
    setState(() {
      _analyzing = true;
      _analyzeError = '';
    });
    try {
      final dataUrl = 'data:image/jpeg;base64,${base64Encode(photo)}';
      final result = await ApiClient.instance.fishId(dataUrl);
      if (!mounted) return;
      setState(() => _predictions = result.top);
      if (result.top.isNotEmpty && _speciesCtrl.text.isEmpty) {
        _speciesCtrl.text = result.top.first.nameRu;
      }
    } on Exception catch (e) {
      if (!mounted) return;
      setState(() => _analyzeError = e.toString());
    } finally {
      if (mounted) setState(() => _analyzing = false);
    }
  }

  Future<void> _save() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() => _uploading = true);
    try {
      if (_photo != null && _photoUrl.isEmpty) {
        _photoUrl = await ApiClient.instance.uploadPhoto(_photo!, 'catch.jpg');
      }
      await ApiClient.instance.addCatch(
        _spot.id,
        speciesName: _speciesCtrl.text.trim(),
        weightKg: double.tryParse(_weightCtrl.text.replaceAll(',', '.')),
        lengthCm: double.tryParse(_lengthCtrl.text.replaceAll(',', '.')),
        photoUrl: _photoUrl.isEmpty ? null : _photoUrl,
        caughtAt: _date,
        notes: _notesCtrl.text.trim().isEmpty ? null : _notesCtrl.text.trim(),
      );
      if (!mounted) return;
      Navigator.of(context).pop(true);
    } on Exception catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Не удалось сохранить: $e')),
      );
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('НОВЫЙ УЛОВ'),
        leading: IconButton(
          icon: const Icon(Icons.close),
          onPressed: () => Navigator.of(context).pop(),
        ),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: TextButton(
              onPressed: _uploading ? null : _save,
              child: _uploading
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text('СОХРАНИТЬ'),
            ),
          ),
        ],
      ),
      body: Form(
        key: _formKey,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            DropdownButtonFormField<Spot>(
              initialValue: _spot,
              isExpanded: true,
              decoration: const InputDecoration(
                labelText: 'ТОЧКА',
                prefixIcon: Icon(Icons.place_outlined, size: 20),
              ),
              items: [
                for (final s in widget.spots)
                  DropdownMenuItem(value: s, child: Text(s.name, overflow: TextOverflow.ellipsis)),
              ],
              onChanged: (s) {
                if (s != null) setState(() => _spot = s);
              },
            ),
            const SizedBox(height: 12),
            Card(
              child: InkWell(
                onTap: () async {
                  final picked = await showDatePicker(
                    context: context,
                    initialDate: _date,
                    firstDate: DateTime(2020, 1, 1),
                    lastDate: DateTime.now().add(const Duration(days: 1)),
                  );
                  if (picked != null) setState(() => _date = picked);
                },
                borderRadius: BorderRadius.circular(10),
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
                  child: Row(
                    children: [
                      const Icon(Icons.calendar_today, size: 16, color: AppColors.accent),
                      const SizedBox(width: 10),
                      Text(
                        '${_date.day.toString().padLeft(2, '0')}.${_date.month.toString().padLeft(2, '0')}.${_date.year}',
                        style: const TextStyle(fontSize: 15),
                      ),
                    ],
                  ),
                ),
              ),
            ),
            const SizedBox(height: 12),
            _photoPicker(),
            const SizedBox(height: 12),
            TextFormField(
              controller: _speciesCtrl,
              decoration: const InputDecoration(labelText: 'ВИД *'),
              validator: (v) => (v == null || v.trim().isEmpty) ? 'Введите вид' : null,
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: TextFormField(
                    controller: _lengthCtrl,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: const InputDecoration(labelText: 'ДЛИНА, см'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextFormField(
                    controller: _weightCtrl,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: const InputDecoration(labelText: 'ВЕС, кг'),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _notesCtrl,
              maxLines: 2,
              decoration: const InputDecoration(labelText: 'ЗАМЕТКИ'),
            ),
            if (_predictions.isNotEmpty) ...[
              const SizedBox(height: 12),
              Text('ОПРЕДЕЛЕНО',
                  style: Theme.of(context).textTheme.labelLarge?.copyWith(
                        color: AppColors.textSecondary,
                        fontSize: 11,
                      )),
              const SizedBox(height: 6),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final p in _predictions)
                    FilterChip(
                      label: Text(
                        '${p.nameRu} · ${(p.confidence * 100).toStringAsFixed(0)}%',
                      ),
                      selected: _speciesCtrl.text == p.nameRu,
                      onSelected: (_) => setState(() => _speciesCtrl.text = p.nameRu),
                      showCheckmark: false,
                      selectedColor: AppColors.accentDim,
                      backgroundColor: AppColors.card,
                      side: BorderSide(
                        color: _speciesCtrl.text == p.nameRu
                            ? AppColors.accent
                            : AppColors.hairline,
                      ),
                      labelStyle: TextStyle(
                        color: _speciesCtrl.text == p.nameRu
                            ? AppColors.accent
                            : AppColors.textPrimary,
                        fontSize: 12,
                      ),
                    ),
                ],
              ),
            ],
            if (_analyzeError.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(_analyzeError,
                    style: const TextStyle(color: AppColors.bad, fontSize: 12)),
              ),
          ],
        ),
      ),
    );
  }

  Widget _photoPicker() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('ФОТО (ОПЦИОНАЛЬНО)',
            style: Theme.of(context).textTheme.labelLarge?.copyWith(
                  color: AppColors.textSecondary,
                  fontSize: 11,
                )),
        const SizedBox(height: 8),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (_photo != null)
              ClipRRect(
                borderRadius: BorderRadius.circular(8),
                child: Image.memory(
                  _photo!,
                  width: 96,
                  height: 96,
                  fit: BoxFit.cover,
                ),
              )
            else
              Container(
                width: 96,
                height: 96,
                decoration: BoxDecoration(
                  color: AppColors.card,
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: AppColors.hairline),
                ),
                child: const Icon(Icons.add_a_photo_outlined,
                    color: AppColors.textMuted, size: 28),
              ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  OutlinedButton.icon(
                    onPressed: _pickPhoto,
                    icon: const Icon(Icons.image_outlined, size: 18),
                    label: const Text('ВЫБРАТЬ ФОТО'),
                    style: OutlinedButton.styleFrom(
                      side: const BorderSide(color: AppColors.hairline),
                    ),
                  ),
                  const SizedBox(height: 8),
                  FilledButton.tonalIcon(
                    onPressed: (_photo == null || _analyzing) ? null : _analyze,
                    icon: _analyzing
                        ? const SizedBox(
                            width: 14,
                            height: 14,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.manage_search, size: 18),
                    label: Text(_analyzing ? 'ОПРЕДЕЛЯЕМ…' : 'ОПРЕДЕЛИТЬ ВИД'),
                    style: FilledButton.styleFrom(
                      backgroundColor: AppColors.accentDim,
                      foregroundColor: AppColors.accent,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ],
    );
  }
}
